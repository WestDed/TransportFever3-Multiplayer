using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MPFever
{
    // Persistent private keys stay on this PC. Only the public key and a signature
    // over a fresh server challenge cross the wire. This is NOT Steam authentication.
    sealed class OwnerIdentity
    {
        readonly RSAParameters key;
        public readonly string Owner;
        public const int MaxProfileBytes = 16384;
        public static string ProfileDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MPFever", "identity");
        public static string ProfilePath(string role) => Path.Combine(ProfileDirectory, role + ".private-owner.lua");
        OwnerIdentity(RSAParameters key) { this.key = key; Owner = Fingerprint(key); }
        static RSA Provider()
        {
            // Framework's CSP KeySize setter does not reliably replace its already
            // generated default key. Select RSA_AES and the size in the constructor.
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return new RSACryptoServiceProvider(2048,new CspParameters(24)) { PersistKeyInCsp=false };
            var rsa=RSA.Create();rsa.KeySize=2048;return rsa;
        }
        public static OwnerIdentity Create()
        {
            using (var rsa = Provider())
            {
                var parameters=rsa.ExportParameters(true);
                if (parameters.Modulus.Length!=256)throw new CryptographicException("Ownership provider did not generate a 2048-bit key");
                return new OwnerIdentity(parameters);
            }
        }
        public static OwnerIdentity Open(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path)); Directory.CreateDirectory(dir);
            // Prevent concurrent first launches from generating different keys or
            // replacing an already existing profile. A corrupt profile never regenerates.
            using (var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                string marker=path+".owner";
                if (!File.Exists(path) && File.Exists(marker)) throw new InvalidDataException("Ownership private key is missing; restore its backup instead of creating another player");
                if (File.Exists(path))
                {
                    using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (f.Length <= 0 || f.Length > MaxProfileBytes) throw new InvalidDataException("Invalid ownership profile size; restore its backup, do not delete it");
                        using (var reader = new StreamReader(f, Wire.Utf8, false))
                        {
                            var t = ProtocolSchema.Table(LuaLit.Parse(reader.ReadToEnd()), "ownership profile");
                            if (ProtocolSchema.Integer(t, "version", 1, 1) != 1 || t.Count != 9) throw new InvalidDataException("Invalid ownership profile version or fields");
                            var p = new RSAParameters { Modulus = Part(t,"n",256), Exponent = Part(t,"e",3), D = Part(t,"d",256), P = Part(t,"p",128), Q = Part(t,"q",128), DP = Part(t,"dp",128), DQ = Part(t,"dq",128), InverseQ = Part(t,"iq",128) };
                            using (var rsa = Provider()) { rsa.ImportParameters(p); if (rsa.KeySize != 2048) throw new InvalidDataException("Invalid ownership key size"); }
                            var identity = new OwnerIdentity(p);
                            // Validate private/public consistency before issuing any proof.
                            var nonce = NewNonce(); var proof = identity.Sign("profile-check", nonce, identity.Owner);
                            if (Verify(identity.PublicTable(), proof, "profile-check", nonce, identity.Owner) != identity.Owner) throw new InvalidDataException("Inconsistent ownership private key");
                            CheckMarker(marker,identity.Owner);
                            return identity;
                        }
                    }
                }
                var created = Create();
                var content = new StringBuilder("{[\"version\"]=1");
                foreach (var pair in new[] { Tuple.Create("n",created.key.Modulus), Tuple.Create("e",created.key.Exponent), Tuple.Create("d",created.key.D), Tuple.Create("p",created.key.P), Tuple.Create("q",created.key.Q), Tuple.Create("dp",created.key.DP), Tuple.Create("dq",created.key.DQ), Tuple.Create("iq",created.key.InverseQ) })
                    content.Append(",[").Append(LuaLit.Quote(pair.Item1)).Append("]=").Append(LuaLit.Quote(Convert.ToBase64String(pair.Item2)));
                content.Append('}');
                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    var bytes = Wire.Utf8.GetBytes(content.ToString());
                    using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes,0,bytes.Length); f.Flush(true); }
                    File.Move(tmp,path); // No overwrite of an existing private identity.
                    CheckMarker(marker,created.Owner);
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
                return created;
            }
        }
        static void CheckMarker(string path,string owner)
        {
            if(File.Exists(path))
            {
                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    if(file.Length!=64)throw new InvalidDataException("Invalid ownership profile marker");
                    using(var reader=new StreamReader(file,Wire.Utf8,false))
                        if(reader.ReadToEnd()!=owner)throw new InvalidDataException("Ownership key changed unexpectedly; restore the original profile backup");
                }
                return;
            }
            var bytes=Wire.Utf8.GetBytes(owner);
            using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
        }
        static byte[] Part(Dictionary<object,object> t, string name, int length)
        {
            if (!(ProtocolSchema.Get(t,name) is string value) || !SnapshotLimits.CanonicalBase64(value,length)) throw new InvalidDataException("Invalid ownership key component: " + name);
            return Convert.FromBase64String(value);
        }
        public string PublicTable() => "{[\"n\"]=" + LuaLit.Quote(Convert.ToBase64String(key.Modulus)) + ",[\"e\"]=" + LuaLit.Quote(Convert.ToBase64String(key.Exponent)) + "}";
        static byte[] Challenge(string epoch, string nonce, string hostOwner)
            => Wire.Utf8.GetBytes("MPFever owner proof v4\n" + epoch + "\n" + nonce + "\n" + hostOwner);
        public string Sign(string epoch, string nonce, string hostOwner)
        {
            using (var rsa = Provider()) { rsa.ImportParameters(key); return Convert.ToBase64String(rsa.SignData(Challenge(epoch,nonce,hostOwner), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)); }
        }
        public static string Verify(string publicTable, string proof, string epoch, string nonce, string hostOwner)
            => Verify(ProtocolSchema.Table(LuaLit.Parse(publicTable),"ownership public key"),proof,epoch,nonce,hostOwner);
        public static string Verify(Dictionary<object,object> t, string proof, string epoch, string nonce, string hostOwner)
        {
            if (t.Count != 2) throw new InvalidDataException("Unexpected ownership public key fields");
            var p = new RSAParameters { Modulus=Part(t,"n",256), Exponent=Part(t,"e",3) };
            if (p.Modulus[0] < 128 || p.Exponent[0] != 1 || p.Exponent[1] != 0 || p.Exponent[2] != 1 || !SnapshotLimits.CanonicalBase64(proof,256)) throw new InvalidDataException("Invalid ownership public key or proof");
            try
            {
                using (var rsa = Provider())
                {
                    rsa.ImportParameters(p);
                    if (!rsa.VerifyData(Challenge(epoch,nonce,hostOwner),Convert.FromBase64String(proof),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)) throw new InvalidDataException("Ownership proof does not match this connection");
                }
            }
            catch (CryptographicException e) { throw new InvalidDataException("Invalid ownership proof",e); }
            return Fingerprint(p);
        }
        static string Fingerprint(RSAParameters p)
        {
            using (var sha=SHA256.Create())
            {
                var bytes = Wire.Utf8.GetBytes("MPFever owner v1\n" + Convert.ToBase64String(p.Modulus) + "\n" + Convert.ToBase64String(p.Exponent));
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            }
        }
        public static string NewNonce()
        { var bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant(); }
    }
}
