using System;
using System.IO;
using System.Text;
using DspAdpcm.Adpcm.Formats.Configuration;
using DspAdpcm.Adpcm.Formats.Structures;
using static DspAdpcm.Helpers;
using DspAdpcm.Pcm;

#if NET20
using DspAdpcm.Compatibility.LinqBridge;
#else
using System.Linq;
#endif

namespace DspAdpcm.Adpcm.Formats
{
    /// <summary>
    /// Represents a BRSTM file.
    /// </summary>
    public class Brstm
    {
        /// <summary>
        /// The underlying <see cref="PcmStream"/> used to build the BRSTM file.
        /// </summary>
        public PcmStream AudioStream { get; set; }

        /// <summary>
        /// Contains various settings used when building the BRSTM file.
        /// </summary>
        public BrstmConfiguration Configuration { get; set; }

        /// <summary>
        /// The single <see cref="AdpcmTrack"/> used to build the BRSTM file.
        /// This library does not support PCM-encoded BRSTM files with more than one track.
        /// </summary>
        private AdpcmTrack Track;

        private int NumSamples => AudioStream.Looping ? LoopEnd : AudioStream.NumSamples;
        private int NumChannels => AudioStream.Channels.Count;
        private int NumTracks => 1;

        private int AlignmentSamples => GetNextMultiple(AudioStream.LoopStart, Configuration.LoopPointAlignment) - AudioStream.LoopStart;
        private int LoopStart => AudioStream.LoopStart + AlignmentSamples;
        private int LoopEnd => AudioStream.LoopEnd + AlignmentSamples;

        private static B_stmCodec Codec => B_stmCodec.Pcm16Bit;
        private byte Looping => (byte)(AudioStream.Looping ? 1 : 0);
        private int AudioDataOffset => DataChunkOffset + 0x20;

        /// <summary>
        /// Size of a single channel's ADPCM audio data with padding when written to a file
        /// </summary>
        private int AudioDataSize => GetNextMultiple(NumSamples * sizeof(short), 0x20);

        private int SamplesPerInterleave => Configuration.SamplesPerInterleave;
        private int InterleaveSize => SamplesPerInterleave * sizeof(short);
        private int InterleaveCount => NumSamples.DivideByRoundUp(SamplesPerInterleave);

        private int LastBlockSamples => NumSamples - ((InterleaveCount - 1) * SamplesPerInterleave);
        private int LastBlockSizeWithoutPadding => LastBlockSamples * sizeof(short);
        private int LastBlockSize => GetNextMultiple(LastBlockSizeWithoutPadding, 0x20);

        private static int RstmHeaderSize => 0x40;

        private int HeadChunkOffset => RstmHeaderSize;
        private int HeadChunkSize => GetNextMultiple(HeadChunkHeaderSize + HeadChunkTableSize +
            HeadChunk1Size + HeadChunk2Size + HeadChunk3Size, 0x20);
        private int HeadChunkHeaderSize => 8;
        private int HeadChunkTableSize => 8 * 3;
        private int HeadChunk1Size => 0x34;
        private int HeadChunk2Size => 4 + (8 * NumTracks) + (TrackInfoSize * NumTracks);
        private BrstmTrackType HeaderType => Configuration.TrackType;
        private int TrackInfoSize => HeaderType == BrstmTrackType.Short ? 4 : 0x0c;
        private int HeadChunk3Size => 4 + (8 * NumChannels) + (ChannelInfoSize * NumChannels);
        private int ChannelInfoSize => 0x38;

        private int DataChunkOffset => RstmHeaderSize + HeadChunkSize;
        private int DataChunkSize => 0x20 + AudioDataSize * NumChannels;

        /// <summary>
        /// The size in bytes of the BRSTM file.
        /// </summary>
        public int FileSize => RstmHeaderSize + HeadChunkSize + DataChunkSize;

        /// <summary>
        /// Initializes a new <see cref="Brstm"/> from an <see cref="PcmStream"/>.
        /// </summary>
        /// <param name="stream">The <see cref="PcmStream"/> used to
        /// create the <see cref="Brstm"/>.</param>
        /// <param name="configuration">A <see cref="BrstmConfiguration"/>
        /// to use for the <see cref="Brstm"/></param>
        public Brstm(PcmStream stream, BrstmConfiguration configuration = null)
        {
            if (stream.Channels.Count < 1)
            {
                throw new InvalidDataException("Stream must have at least one channel ");
            }

            AudioStream = stream;
            Track = new AdpcmTrack {
                NumChannels = 2,
                ChannelLeft = 0,
                ChannelRight = stream.NumChannels >= 2 ? 1 : 0
            };
            Configuration = configuration ?? new BrstmConfiguration();
        }

        /// <summary>
        /// Initializes a new <see cref="Brstm"/> by parsing an existing
        /// BRSTM file.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> containing 
        /// the BRSTM file. Must be seekable.</param>
        /// <param name="configuration">A <see cref="BrstmConfiguration"/>
        /// to use for the <see cref="Brstm"/></param>
        public Brstm(Stream stream, BrstmConfiguration configuration = null)
        {
            ReadStream(stream, configuration);
        }

        /// <summary>
        /// Initializes a new <see cref="Brstm"/> by parsing an existing
        /// BRSTM file.
        /// </summary>
        /// <param name="file">A <c>byte[]</c> containing 
        /// the BRSTM file.</param>
        /// <param name="configuration">A <see cref="BrstmConfiguration"/>
        /// to use for the <see cref="Brstm"/></param>
        public Brstm(byte[] file, BrstmConfiguration configuration = null)
        {
            using (var stream = new MemoryStream(file))
            {
                ReadStream(stream, configuration);
            }
        }

        /// <summary>
        /// Parses the header of a BRSTM file and returns the metadata
        /// and structure data of that file.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> containing 
        /// the BRSTM file. Must be seekable.</param>
        /// <returns>A <see cref="BrstmStructure"/> containing
        /// the data from the BRSTM header.</returns>
        public static BrstmStructure ReadMetadata(Stream stream)
        {
            CheckStream(stream, RstmHeaderSize);
            return ReadBrstmFile(stream, false);
        }

        private void ReadStream(Stream stream, BrstmConfiguration configuration = null)
        {
            CheckStream(stream, RstmHeaderSize);

            BrstmStructure brstm = ReadBrstmFile(stream);
            AudioStream = GetPcmStream(brstm);
            Track = brstm.Tracks.Single();
            Configuration = configuration ?? GetConfiguration(brstm);
        }

        /// <summary>
        /// Builds a BRSTM file from the current <see cref="AudioStream"/>.
        /// </summary>
        /// <returns>A BRSTM file</returns>
        public byte[] GetFile()
        {
            var file = new byte[FileSize];
            var stream = new MemoryStream(file);
            WriteFile(stream);
            return file;
        }

        /// <summary>
        /// Writes the BRSTM file to a <see cref="Stream"/>.
        /// The file is written starting at the beginning
        /// of the <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to write the
        /// BRSTM to.</param>
        public void WriteFile(Stream stream)
        {
            if (stream.Length != FileSize)
            {
                try
                {
                    stream.SetLength(FileSize);
                }
                catch (NotSupportedException ex)
                {
                    throw new ArgumentException("Stream is too small.", nameof(stream), ex);
                }
            }

            using (BinaryWriter writer = GetBinaryWriter(stream, Endianness.BigEndian))
            {
                stream.Position = 0;
                GetRstmHeader(writer);
                stream.Position = HeadChunkOffset;
                GetHeadChunk(writer);
                stream.Position = DataChunkOffset;
                GetDataChunk(writer);
            }
        }

        private void GetRstmHeader(BinaryWriter writer)
        {
            writer.WriteUTF8("RSTM");
            writer.Write((ushort)0xfeff); //Endianness
            writer.Write((short)0x0100); //BRSTM format version
            writer.Write(FileSize);
            writer.Write((short)RstmHeaderSize);
            writer.Write((short)2); // NumEntries
            writer.Write(HeadChunkOffset);
            writer.Write(HeadChunkSize);
            writer.Write(DataChunkOffset);
            writer.Write(DataChunkSize);
        }

        private void GetHeadChunk(BinaryWriter writer)
        {
            writer.WriteUTF8("HEAD");
            writer.Write(HeadChunkSize);

            writer.Write(0x01000000);
            writer.Write(HeadChunkTableSize); //Chunk 1 offset
            writer.Write(0x01000000);
            writer.Write(HeadChunkTableSize + HeadChunk1Size); //Chunk 2 offset
            writer.Write(0x01000000);
            writer.Write(HeadChunkTableSize + HeadChunk1Size + HeadChunk2Size); //Chunk 3 offset

            GetHeadChunk1(writer);
            GetHeadChunk2(writer);
            GetHeadChunk3(writer);
        }

        private void GetHeadChunk1(BinaryWriter writer)
        {
            writer.Write((byte)Codec);
            writer.Write(Looping);
            writer.Write((byte)NumChannels);
            writer.Write((byte)0); //padding
            writer.Write((ushort)AudioStream.SampleRate);
            writer.Write((short)0);//padding
            writer.Write(LoopStart);
            writer.Write(NumSamples);
            writer.Write(AudioDataOffset);
            writer.Write(InterleaveCount);
            writer.Write(InterleaveSize);
            writer.Write(SamplesPerInterleave);
            writer.Write(LastBlockSizeWithoutPadding);
            writer.Write(LastBlockSamples);
            writer.Write(LastBlockSize);
            writer.Write(0);
            writer.Write(0);
        }

        private void GetHeadChunk2(BinaryWriter writer)
        {
            writer.Write((byte)NumTracks);
            writer.Write((byte)(HeaderType == BrstmTrackType.Short ? 0 : 1));
            writer.Write((short)0);

            int baseOffset = HeadChunkTableSize + HeadChunk1Size + 4;
            int offsetTableSize = NumTracks * 8;

            for (int i = 0; i < NumTracks; i++)
            {
                writer.Write(HeaderType == BrstmTrackType.Short ? 0x01000000 : 0x01010000);
                writer.Write(baseOffset + offsetTableSize + TrackInfoSize * i);
            }
            
            if (HeaderType == BrstmTrackType.Standard)
            {
                writer.Write((byte)0x7f);
                writer.Write((byte)0x40);
                writer.Write((short)0);
                writer.Write(0);
            }
            writer.Write((byte)AudioStream.NumChannels);
            writer.Write((byte)0); //First channel ID
            writer.Write((byte)1); //Second channel ID
            writer.Write((byte)0);
        }

        private void GetHeadChunk3(BinaryWriter writer)
        {
            writer.Write((byte)NumChannels);
            writer.Write((byte)0); //padding
            writer.Write((short)0); //padding

            int baseOffset = HeadChunkTableSize + HeadChunk1Size + HeadChunk2Size + 4;
            int offsetTableSize = NumChannels * 8;

            for (int i = 0; i < NumChannels; i++)
            {
                writer.Write(0x01000000);
                writer.Write(baseOffset + offsetTableSize + ChannelInfoSize * i);
            }

            for (int i = 0; i < NumChannels; i++)
            {
                writer.Write(0x01000000);
                writer.Write(0x00000000);
            }
        }

        private void GetDataChunk(BinaryWriter writer)
        {
            writer.WriteUTF8("DATA");
            writer.Write(DataChunkSize);
            writer.Write(0x18);

            writer.BaseStream.Position = AudioDataOffset;

            byte[][] channels = AudioStream.Channels.Select(x => x.GetAudioData(Endianness.BigEndian)).ToArray();

            channels.Interleave(writer.BaseStream, InterleaveSize, AudioDataSize);
        }

        private static BrstmStructure ReadBrstmFile(Stream stream, bool readAudioData = true)
        {
            using (BinaryReader reader = GetBinaryReader(stream, Endianness.BigEndian))
            {
                if (Encoding.UTF8.GetString(reader.ReadBytes(4), 0, 4) != "RSTM")
                {
                    throw new InvalidDataException("File has no RSTM header");
                }

                var structure = new BrstmStructure();

                ParseRstmHeader(reader, structure);
                ParseHeadChunk(reader, structure);
                ParseDataChunk(reader, structure, readAudioData);

                return structure;
            }
        }

        private static BrstmConfiguration GetConfiguration(BrstmStructure structure)
        {
            return new BrstmConfiguration()
            {
                SamplesPerInterleave = structure.SamplesPerInterleave,
                TrackType = structure.HeaderType
            };
        }

        private static PcmStream GetPcmStream(BrstmStructure structure)
        {
            var audioStream = new PcmStream(structure.NumSamples, structure.SampleRate);
            if (structure.Looping)
            {
                audioStream.SetLoop(structure.LoopStart, structure.NumSamples);
            }

            for (int c = 0; c < structure.NumChannels; c++)
            {
                var channel = new PcmChannel(structure.NumSamples, structure.AudioData[c], Endianness.BigEndian);
                audioStream.Channels.Add(channel);
            }

            return audioStream;
        }

        private static void ParseRstmHeader(BinaryReader reader, BrstmStructure structure)
        {
            reader.Expect((ushort)0xfeff);
            structure.Version = reader.ReadInt16();
            structure.FileSize = reader.ReadInt32();

            if (reader.BaseStream.Length < structure.FileSize)
            {
                throw new InvalidDataException("Actual file length is less than stated length");
            }

            structure.RstmHeaderSize = reader.ReadInt16();
            structure.RstmHeaderSections = reader.ReadInt16();

            structure.HeadChunkOffset = reader.ReadInt32();
            structure.HeadChunkSizeRstm = reader.ReadInt32();
            structure.AdpcChunkOffset = reader.ReadInt32();
            structure.AdpcChunkSizeRstm = reader.ReadInt32();
            structure.DataChunkOffset = reader.ReadInt32();
            structure.DataChunkSizeRstm = reader.ReadInt32();
        }

        private static void ParseHeadChunk(BinaryReader reader, BrstmStructure structure)
        {
            reader.BaseStream.Position = structure.HeadChunkOffset;

            if (Encoding.UTF8.GetString(reader.ReadBytes(4), 0, 4) != "HEAD")
            {
                throw new InvalidDataException("Unknown or invalid HEAD chunk");
            }

            structure.HeadChunkSize = reader.ReadInt32();
            if (structure.HeadChunkSize != structure.HeadChunkSizeRstm)
            {
                throw new InvalidDataException("HEAD chunk size in RSTM header doesn't match size in HEAD header");
            }

            reader.Expect(0x01000000);
            structure.HeadChunk1Offset = reader.ReadInt32();
            reader.Expect(0x01000000);
            structure.HeadChunk2Offset = reader.ReadInt32();
            reader.Expect(0x01000000);
            structure.HeadChunk3Offset = reader.ReadInt32();

            ParseHeadChunk1(reader, structure);
            ParseHeadChunk2(reader, structure);
            ParseHeadChunk3(reader, structure);
        }

        private static void ParseHeadChunk1(BinaryReader reader, BrstmStructure structure)
        {
            reader.BaseStream.Position = structure.HeadChunkOffset + 8 + structure.HeadChunk1Offset;
            structure.Codec = (B_stmCodec)reader.ReadByte();
            if (structure.Codec != B_stmCodec.Pcm16Bit)
            {
                throw new NotSupportedException("File must contain 16-bit PCM encoded audio");
            }

            structure.Looping = reader.ReadByte() == 1;
            structure.NumChannels = reader.ReadByte();
            reader.BaseStream.Position += 1;

            structure.SampleRate = reader.ReadUInt16();
            reader.BaseStream.Position += 2;

            structure.LoopStart = reader.ReadInt32();
            structure.NumSamples = reader.ReadInt32();

            structure.AudioDataOffset = reader.ReadInt32();
            structure.InterleaveCount = reader.ReadInt32();
            structure.InterleaveSize = reader.ReadInt32();
            structure.SamplesPerInterleave = reader.ReadInt32();
            structure.LastBlockSizeWithoutPadding = reader.ReadInt32();
            structure.LastBlockSamples = reader.ReadInt32();
            structure.LastBlockSize = reader.ReadInt32();
            structure.SamplesPerSeekTableEntry = reader.ReadInt32();
        }

        private static void ParseHeadChunk2(BinaryReader reader, BrstmStructure structure)
        {
            int baseOffset = structure.HeadChunkOffset + 8;
            reader.BaseStream.Position = baseOffset + structure.HeadChunk2Offset;

            int numTracks = reader.ReadByte();
            int[] trackOffsets = new int[numTracks];

            structure.HeaderType = reader.ReadByte() == 0 ? BrstmTrackType.Short : BrstmTrackType.Standard;
            int marker = structure.HeaderType == BrstmTrackType.Short ? 0x01000000 : 0x01010000;

            reader.BaseStream.Position += 2;
            for (int i = 0; i < numTracks; i++)
            {
                reader.Expect(marker);
                trackOffsets[i] = reader.ReadInt32();
            }

            foreach (int offset in trackOffsets)
            {
                reader.BaseStream.Position = baseOffset + offset;
                var track = new AdpcmTrack();

                if (structure.HeaderType == BrstmTrackType.Standard)
                {
                    track.Volume = reader.ReadByte();
                    track.Panning = reader.ReadByte();
                    reader.BaseStream.Position += 6;
                }

                track.NumChannels = reader.ReadByte();
                track.ChannelLeft = reader.ReadByte();
                track.ChannelRight = reader.ReadByte();

                structure.Tracks.Add(track);
            }
        }

        private static void ParseHeadChunk3(BinaryReader reader, BrstmStructure structure)
        {
            int baseOffset = structure.HeadChunkOffset + 8;
            reader.BaseStream.Position = baseOffset + structure.HeadChunk3Offset;

            reader.Expect((byte)structure.NumChannels);
            reader.BaseStream.Position += 3;

            for (int i = 0; i < structure.NumChannels; i++)
            {
                var channel = new B_stmChannelInfo();
                reader.Expect(0x01000000);
                channel.Offset = reader.ReadInt32();
                structure.Channels.Add(channel);
            }

            foreach (B_stmChannelInfo channel in structure.Channels)
            {
                reader.BaseStream.Position = baseOffset + channel.Offset;
                reader.Expect(0x01000000);
                int coefsOffset = reader.ReadInt32();
                reader.BaseStream.Position = baseOffset + coefsOffset;

                channel.Coefs = Enumerable.Range(0, 16).Select(x => reader.ReadInt16()).ToArray();
                channel.Gain = reader.ReadInt16();
                channel.PredScale = reader.ReadInt16();
                channel.Hist1 = reader.ReadInt16();
                channel.Hist2 = reader.ReadInt16();
                channel.LoopPredScale = reader.ReadInt16();
                channel.LoopHist1 = reader.ReadInt16();
                channel.LoopHist2 = reader.ReadInt16();
            }
        }
        
        private static void ParseDataChunk(BinaryReader reader, BrstmStructure structure, bool readAudioData)
        {
            reader.BaseStream.Position = structure.DataChunkOffset;

            if (Encoding.UTF8.GetString(reader.ReadBytes(4), 0, 4) != "DATA")
            {
                throw new InvalidDataException("Unknown or invalid DATA chunk");
            }
            structure.DataChunkSize = reader.ReadInt32();

            if (structure.DataChunkSizeRstm != structure.DataChunkSize)
            {
                throw new InvalidDataException("DATA chunk size in RSTM header doesn't match size in DATA header");
            }

            if (!readAudioData) return;

            reader.BaseStream.Position = structure.AudioDataOffset;
            int audioDataLength = structure.DataChunkSize - (structure.AudioDataOffset - structure.DataChunkOffset);

            structure.AudioData = reader.BaseStream.DeInterleave(audioDataLength, structure.InterleaveSize,
                structure.NumChannels, structure.NumSamples * sizeof(short));
        }
    }
}
