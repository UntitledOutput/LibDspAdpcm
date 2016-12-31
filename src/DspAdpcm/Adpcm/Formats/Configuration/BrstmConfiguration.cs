using DspAdpcm.Adpcm.Formats.Internal;
using DspAdpcm.Adpcm.Formats.Structures;

namespace DspAdpcm.Adpcm.Formats.Configuration
{
    /// <summary>
    /// Contains the options used to build the BRSTM file.
    /// </summary>
    public class BrstmConfiguration : B_stmPcm16Configuration
    {
        /// <summary>
        /// The type of track description to be used when building the 
        /// BRSTM header.
        /// Default is <see cref="BrstmTrackType.Standard"/>
        /// </summary>
        public BrstmTrackType TrackType { get; set; } = BrstmTrackType.Standard;
    }
}
