using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MPFever
{
    // OFFLINE FOUNDATION: compiled, exercised, but deliberately not wired into
    // MainForm's native/GUI action schedule before the engine cutover gate.
    sealed class OrderedCommand
    {
        public readonly string Epoch, Peer, Uid, Payload;
        public readonly long Sequence;
        public readonly int Company;
        public OrderedCommand(string epoch, long sequence, string peer, string uid, int company, string payload)
        { Epoch=epoch; Sequence=sequence; Peer=peer; Uid=uid; Company=company; Payload=payload; }
    }

    sealed class CommandJournal : IDisposable
    {
        const int HeaderBytes = 40, MaxRecordBytes = LuaLit.MaxBytes + 512;
        public const long DefaultMaxBytes = 256L * 1024 * 1024;
        readonly object gate = new object();
        readonly FileStream file;
        readonly long byteLimit;
        readonly string epoch;
        readonly Dictionary<string,int> companies = new Dictionary<string,int>();
        sealed class Entry { public long Offset, Sequence; public string Digest; public byte[] Previous; }
        readonly List<Entry> entries = new List<Entry>();
        readonly Dictionary<string,Entry> seen = new Dictionary<string,Entry>();
        byte[] lastHash = new byte[32];
        bool frozen, faulted, disposed;
        public long RecoveredTailBytes { get; private set; }
        public long LastSequence { get { lock(gate) return entries.Count; } }
        public string Epoch => epoch;
        public CommandJournal(string path, string epoch, long byteLimit = DefaultMaxBytes)
        {
            if (!SnapshotLimits.Hex(epoch,32) || byteLimit < HeaderBytes) throw new InvalidDataException("Invalid journal epoch or budget");
            this.epoch=epoch; this.byteLimit=byteLimit;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            file = new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);
            try
            {
                if(file.Length > byteLimit) throw new InvalidDataException("Journal exceeds disk budget");
                if(file.Length == 0)
                {
                    var header = Encoding.ASCII.GetBytes("MPFJ0001"+epoch);
                    file.Write(header,0,header.Length); file.Flush(true);
                }
                Recover();
            }
            catch { file.Dispose(); throw; }
        }
        public void Bind(string peer, int company)
        {
            if (string.IsNullOrEmpty(peer) || peer.Length > 64 || peer.Any(char.IsControl) || company < 1 || company > 4096) throw new InvalidDataException("Invalid trusted peer binding");
            lock(gate)
            {
                EnsureOpen();
                if(companies.TryGetValue(peer,out var old) && old != company) throw new InvalidDataException("Cannot rebind an active peer");
                companies[peer]=company;
            }
        }
        public OrderedCommand Accept(string expectedEpoch, Msg message)
        {
            // Caller must supply the established session peer, not a wire's From.
            var data = ProtocolSchema.ValidateClient(message);
            if(message.Kind != "act") throw new InvalidDataException("Journal accepts actions only");
            string uid = ProtocolSchema.Text(data,"uid",128);
            int co=(int)ProtocolSchema.Integer(data,"co",1,4096);
            ProtocolSchema.CheckPlayerReferences(ProtocolSchema.Table(ProtocolSchema.Get(data,"args"),"args"),co);
            string key=message.From+"\n"+uid;
            byte[] payloadBytes=Wire.Utf8.GetBytes(message.Payload);
            string digest=Digest(payloadBytes);
            lock(gate)
            {
                EnsureOpen();
                if(expectedEpoch != epoch) throw new InvalidDataException("Stale journal epoch");
                if(!companies.TryGetValue(message.From,out var allowed) || allowed != co) throw new InvalidDataException("Unbound or foreign company");
                if(seen.TryGetValue(key,out var previous))
                {
                    if(previous.Digest != digest) throw new InvalidDataException("Conflicting duplicate command uid");
                    return ReadAt(previous.Offset).Command;
                }
                if(frozen) throw new InvalidDataException("Journal frozen for snapshot cutover");
                if(entries.Count >= 100000) throw new InvalidDataException("Journal command capacity reached");
                var command=new OrderedCommand(epoch,entries.Count+1L,message.From,uid,co,message.Payload);
                byte[] body=Encode(command), hash=ChainHash(lastHash,body);
                long offset=file.Length;
                if(offset+4+32+body.Length+32 > byteLimit) throw new InvalidDataException("Journal disk budget exhausted; checkpoint required");
                try
                {
                    file.Position=offset;
                    using(var writer=new BinaryWriter(file,Encoding.UTF8,true))
                    { writer.Write(body.Length); writer.Write(lastHash); writer.Write(body); writer.Write(hash); }
                    file.Flush(true); // durability BEFORE the returned acceptance/sequence
                }
                catch
                {
                    faulted=true; // no more accepts even if rollback itself fails
                    try { file.SetLength(offset); file.Flush(true); } catch(IOException) { }
                    throw;
                }
                var entry=new Entry {Offset=offset,Sequence=command.Sequence,Digest=digest,Previous=lastHash};
                entries.Add(entry); seen.Add(key,entry); lastHash=hash;
                return command;
            }
        }
        public long Freeze() { lock(gate) { EnsureOpen(); frozen=true; return entries.Count; } }
        public IEnumerable<OrderedCommand> Replay(string expectedEpoch,long after,int count=64)
        {
            Entry[] window;
            lock(gate)
            {
                EnsureOpen();
                if(expectedEpoch != epoch || after < 0 || after > entries.Count || count < 1 || count > 64) throw new InvalidDataException("Invalid replay frontier or epoch");
                window=entries.Skip((int)after).Take(count).ToArray();
            }
            foreach(var entry in window)
            {
                OrderedCommand command;
                lock(gate)
                {
                    EnsureOpen(); var record=ReadAt(entry.Offset);
                    if(!record.Previous.SequenceEqual(entry.Previous) || record.Command.Sequence != entry.Sequence) throw new InvalidDataException("Journal changed during replay");
                    command=record.Command;
                }
                yield return command;
            }
        }
        sealed class Record { public OrderedCommand Command; public byte[] Previous,Hash; }
        Record ReadAt(long offset)
        {
            file.Position=offset;
            using(var reader=new BinaryReader(file,Encoding.UTF8,true))
            {
                int length=reader.ReadInt32();
                if(length < 24 || length > MaxRecordBytes) throw new InvalidDataException("Invalid journal record size");
                var prev=ReadExact(reader,32); var body=ReadExact(reader,length); var hash=ReadExact(reader,32);
                if(!ChainHash(prev,body).SequenceEqual(hash)) throw new InvalidDataException("Journal record hash mismatch");
                using(var input=new MemoryStream(body,false))
                using(var r=new BinaryReader(input,Encoding.UTF8))
                {
                    long seq=r.ReadInt64(); int co=r.ReadInt32();
                    string peer=ReadText(r,256),uid=ReadText(r,512),payload=ReadText(r,LuaLit.MaxBytes);
                    if(input.Position != input.Length || seq < 1 || co < 1 || co > 4096) throw new InvalidDataException("Invalid journal record fields");
                    var m=Msg.Make("act",peer,payload); var t=ProtocolSchema.ValidateClient(m);
                    if(ProtocolSchema.Text(t,"uid",128) != uid || ProtocolSchema.Integer(t,"co",1,4096) != co) throw new InvalidDataException("Inconsistent stored command");
                    return new Record { Command=new OrderedCommand(epoch,seq,peer,uid,co,payload),Previous=prev,Hash=hash };
                }
            }
        }
        void Recover()
        {
            file.Position=0;
            using(var reader=new BinaryReader(file,Encoding.UTF8,true))
            {
                if(file.Length < HeaderBytes || Encoding.ASCII.GetString(ReadExact(reader,HeaderBytes)) != "MPFJ0001"+epoch) throw new InvalidDataException("Journal header/version/epoch mismatch");
                while(file.Position < file.Length)
                {
                    long offset=file.Position;
                    if(file.Length-offset < 4) { RepairTail(offset); break; }
                    int length=reader.ReadInt32();
                    if(length < 24 || length > MaxRecordBytes) throw new InvalidDataException("Invalid journal length prefix");
                    if(file.Length-offset < 4L+32+length+32) { RepairTail(offset); break; }
                    var record=ReadAt(offset); var c=record.Command;
                    string key=c.Peer+"\n"+c.Uid;
                    if(c.Sequence != entries.Count+1L || !record.Previous.SequenceEqual(lastHash) || seen.ContainsKey(key)) throw new InvalidDataException("Journal sequence, chain or duplicate corruption");
                    if(entries.Count >= 100000) throw new InvalidDataException("Journal exceeds command capacity");
                    var entry=new Entry {Offset=offset,Sequence=c.Sequence,Digest=Digest(Wire.Utf8.GetBytes(c.Payload)),Previous=record.Previous};
                    entries.Add(entry);seen.Add(key,entry);lastHash=record.Hash;
                }
            }
        }
        void RepairTail(long offset)
        {
            // Only an incomplete final frame can be discarded. Complete corrupt
            // records, even at EOF, fail closed instead of silently losing history.
            RecoveredTailBytes=file.Length-offset; file.SetLength(offset);file.Flush(true);
        }
        static byte[] Encode(OrderedCommand command)
        {
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
                { writer.Write(command.Sequence);writer.Write(command.Company);WriteText(writer,command.Peer);WriteText(writer,command.Uid);WriteText(writer,command.Payload); }
                return stream.ToArray();
            }
        }
        static void WriteText(BinaryWriter writer,string value) { var bytes=Wire.Utf8.GetBytes(value);writer.Write(bytes.Length);writer.Write(bytes); }
        static string ReadText(BinaryReader reader,int max)
        {
            int length=reader.ReadInt32();
            if(length < 1 || length > max || length > reader.BaseStream.Length-reader.BaseStream.Position) throw new InvalidDataException("Invalid journal string length");
            return Wire.Utf8.GetString(ReadExact(reader,length));
        }
        static byte[] ReadExact(BinaryReader reader,int count) { var bytes=reader.ReadBytes(count);if(bytes.Length != count) throw new InvalidDataException("Truncated journal record");return bytes; }
        static byte[] ChainHash(byte[] previous,byte[] body)
        { var bytes=new byte[previous.Length+body.Length];Buffer.BlockCopy(previous,0,bytes,0,previous.Length);Buffer.BlockCopy(body,0,bytes,previous.Length,body.Length);using(var sha=SHA256.Create())return sha.ComputeHash(bytes); }
        static string Digest(byte[] bytes) { using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(bytes)); }
        void EnsureOpen() { if(disposed)throw new ObjectDisposedException(nameof(CommandJournal));if(faulted)throw new IOException("Journal faulted; recovery required"); }
        public void Dispose() { lock(gate) { disposed=true;file.Dispose(); } }
    }

    // Pure recovery gate. A load receipt is insufficient without the complete
    // expected peer set acknowledging the exact epoch/frontier/hash.
    sealed class RecoveryBarrier
    {
        readonly object gate=new object();
        readonly HashSet<string> required,ready=new HashSet<string>();
        readonly string epoch,hash;
        readonly long frontier;
        bool failed,running;
        public RecoveryBarrier(string epoch,long frontier,string hash,IEnumerable<string> peers)
        {
            if(!SnapshotLimits.Hex(epoch,32) || !SnapshotLimits.Hex(hash,64) || frontier < 0) throw new InvalidDataException("Invalid recovery frontier");
            this.epoch=epoch;this.frontier=frontier;this.hash=hash;required=new HashSet<string>(peers);
            if(required.Count == 0 || required.Count > Wire.MaxPeers+1 || required.Any(string.IsNullOrEmpty)) throw new InvalidDataException("Invalid recovery peer set");
        }
        public void Ready(string peer,string ackEpoch,long ackFrontier,string ackHash)
        {
            lock(gate)
            {
                if(failed || running || !required.Contains(peer) || ackEpoch != epoch || ackFrontier != frontier || ackHash != hash) throw new InvalidDataException("Stale, foreign or inconsistent recovery acknowledgement");
                ready.Add(peer);
            }
        }
        public bool CanResume { get { lock(gate) return !failed && !running && ready.SetEquals(required); } }
        public void Fail() { lock(gate) failed=true; }
        public void Resume() { lock(gate) { if(!CanResume)throw new InvalidDataException("Recovery quorum incomplete or failed");running=true; } }
    }

    // In-memory replay frontier only. Persisting game effects and this frontier
    // atomically, and asynchronous engine callbacks, are part of the engine adapter.
    sealed class ReplayCursor
    {
        readonly object gate=new object();
        readonly string epoch;
        readonly Dictionary<long,string> recent=new Dictionary<long,string>();
        long frontier;
        bool faulted;
        public ReplayCursor(string epoch,long frontier=0)
        { if(!SnapshotLimits.Hex(epoch,32) || frontier<0)throw new InvalidDataException("Invalid replay cursor");this.epoch=epoch;this.frontier=frontier; }
        public long Frontier { get { lock(gate)return frontier; } }
        public bool Apply(OrderedCommand command,Action<OrderedCommand> apply)
        {
            lock(gate)
            {
                if(faulted || command.Epoch != epoch)throw new InvalidDataException("Faulted cursor or stale replay epoch");
                string identity=command.Peer+"\n"+command.Uid+"\n"+command.Company+"\n"+command.Payload;
                if(command.Sequence<=frontier)
                {
                    if(recent.TryGetValue(command.Sequence,out var old) && old!=identity)throw new InvalidDataException("Conflicting replay sequence");
                    return false;
                }
                if(command.Sequence!=frontier+1)throw new InvalidDataException("Replay gap");
                try { apply(command); }
                catch { faulted=true;throw; } // effect may have happened: do not retry blindly
                frontier=command.Sequence;recent[frontier]=identity;
                recent.Remove(frontier-256);
                return true;
            }
        }
    }
}
