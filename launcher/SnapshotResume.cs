using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MPFever
{
    // One bounded partial per persistent client profile. Metadata never contains
    // paths or executable data. Every advertised chunk is rehashed from disk.
    sealed partial class SnapshotReceiver
    {
        const int ResumeMagic=0x4d504631;
        readonly bool resumable;
        byte[][] digests;
        DateTime checkpointed;
        string Metadata => Path.Combine(directory,"download.meta");
        public string HaveMask { get { lock(gate)return seen==null?null:new string(seen.Select(v=>v?'1':'0').ToArray()); } }
        // A cheap ordering hint only. Begin still revalidates metadata and all
        // stored bytes; this does not authorize a receipt or a game load.
        public static bool HasPartial(string directory,SnapshotManifest manifest)
        {
            try
            {
                string path=Path.Combine(directory,"download.part"),meta=Path.Combine(directory,"download.meta");
                if(!File.Exists(path) || new FileInfo(path).Length!=manifest.Bytes || !File.Exists(meta) || new FileInfo(meta).Length>140000)return false;
                using(var reader=new BinaryReader(File.OpenRead(meta)))
                    return reader.ReadInt32()==ResumeMagic && reader.ReadInt64()==manifest.Bytes && reader.ReadInt32()==manifest.Parts
                        && reader.ReadBytes(64).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(manifest.Sha256));
            }
            catch(IOException) {return false;}catch(UnauthorizedAccessException) {return false;}
        }
        void DeleteMetadata()
        {
            if(!resumable)return;
            if(File.Exists(Metadata))File.Delete(Metadata);
            if(File.Exists(Metadata+".tmp"))File.Delete(Metadata+".tmp");
        }
        void OpenResumable(SnapshotManifest next,Func<bool> cancelled)
        {
            path=Path.Combine(directory,"download.part");manifest=next;
            seen=new bool[next.Parts];digests=new byte[next.Parts][];received=0;
            begun=touched=checkpointed=now();
            try
            {
                byte[][] stored=null;
                if(File.Exists(path) && new FileInfo(path).Length==next.Bytes && File.Exists(Metadata)
                    && File.GetLastWriteTimeUtc(Metadata)>now().AddDays(-7))
                {
                    try
                    {
                        // <=4096*(1+32)+header, checked before reading any records.
                        if(new FileInfo(Metadata).Length>140000)throw new InvalidDataException("Oversized partial metadata");
                        using(var reader=new BinaryReader(File.OpenRead(Metadata)))
                        {
                            if(reader.ReadInt32()!=ResumeMagic || reader.ReadInt64()!=next.Bytes || reader.ReadInt32()!=next.Parts
                                || !reader.ReadBytes(64).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(next.Sha256)))throw new InvalidDataException("Different partial snapshot");
                            stored=new byte[next.Parts][];
                            for(int i=0;i<stored.Length;i++)
                            {
                                byte present=reader.ReadByte();
                                if(present>1)throw new InvalidDataException("Invalid partial flag");
                                if(present==1) {stored[i]=reader.ReadBytes(32);if(stored[i].Length!=32)throw new EndOfStreamException();}
                            }
                            if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Trailing partial metadata");
                        }
                    }
                    catch(IOException) {stored=null;}
                    catch(InvalidDataException) {stored=null;}
                }
                file=new FileStream(path,stored==null?FileMode.Create:FileMode.Open,FileAccess.ReadWrite,FileShare.None);
                file.SetLength(next.Bytes);
                if(stored!=null)
                {
                    var bytes=new byte[SnapshotLimits.ChunkBytes];
                    for(int i=0;i<stored.Length;i++)
                    {
                        if(cancelled())throw new OperationCanceledException();
                        if(stored[i]==null)continue;
                        file.Position=(long)i*SnapshotLimits.ChunkBytes;
                        int wanted=(int)Math.Min(bytes.Length,next.Bytes-file.Position),got=0;
                        while(got<wanted) {int n=file.Read(bytes,got,wanted-got);if(n==0)break;got+=n;}
                        using(var sha=SHA256.Create())
                            if(got==wanted && sha.ComputeHash(bytes,0,got).SequenceEqual(stored[i]))
                            {seen[i]=true;digests[i]=stored[i];received++;}
                    }
                }
                Checkpoint();
            }
            catch {Clear();throw;}
        }
        void Checkpoint()
        {
            // Flush data before committing the map. Up to five seconds may be
            // retransmitted after a crash; unflushed bytes are never trusted.
            file.Flush(true);
            using(var stream=new FileStream(Metadata+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
            using(var writer=new BinaryWriter(stream))
            {
                writer.Write(ResumeMagic);writer.Write(manifest.Bytes);writer.Write(manifest.Parts);
                writer.Write(System.Text.Encoding.ASCII.GetBytes(manifest.Sha256));
                for(int i=0;i<seen.Length;i++) {writer.Write((byte)(seen[i]?1:0));if(seen[i])writer.Write(digests[i]);}
                writer.Flush();stream.Flush(true);
            }
            if(File.Exists(Metadata))File.Replace(Metadata+".tmp",Metadata,null);else File.Move(Metadata+".tmp",Metadata);
            checkpointed=now();
        }
        // Idle expiry: checkpoint, then forget the transfer in memory only. A later
        // part fails as inactive and the next offer resumes from the files.
        void Retain()
        {
            try {Checkpoint();}
            catch {Clear();throw;}
            file?.Dispose();file=null;path=null;manifest=null;seen=null;digests=null;received=0;
        }
        // Network loss keeps only verified data. Explicit disposal/cancel still
        // removes an active transfer, preserving the old non-resumable contract.
        public void Suspend()
        {
            lock(gate)
            {
                if(disposed)return;
                try {if(resumable && manifest!=null)Checkpoint();else Clear();}
                catch {Clear();throw;}
                finally {disposed=true;timer.Dispose();file?.Dispose();file=null;path=null;manifest=null;seen=null;digests=null;received=0;}
            }
        }
    }
}
