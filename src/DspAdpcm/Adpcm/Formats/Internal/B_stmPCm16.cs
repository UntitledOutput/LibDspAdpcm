using System;
using static DspAdpcm.Helpers;

namespace DspAdpcm.Adpcm.Formats.Internal
{
    /// <summary>
    /// Contains the options used to build audio files.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public abstract class B_stmPcm16Configuration
    {
        internal B_stmPcm16Configuration() { }
        private int _samplesPerInterleave = 0x1000;

        /// <summary>
        /// The number of samples in each block when interleaving
        /// the audio data in the audio file.
        /// Default is 4,096 (0x1000).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if value is negative .</exception>
        public int SamplesPerInterleave
        {
            get { return _samplesPerInterleave; }
            set
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "Number of samples per interleave must be positive");
                }
                _samplesPerInterleave = value;
            }
        }

        /// <summary>
        /// When building the audio file, the loop points and audio will
        /// be adjusted so that the start loop point is a multiple of
        /// this number. Default is 4,096 (0x1000).
        /// </summary>
        public int LoopPointAlignment { get; set; } = 0x1000;
    }
}
