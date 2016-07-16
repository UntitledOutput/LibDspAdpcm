﻿using System;
using System.Diagnostics;
using System.IO;
using DspAdpcm.Encode;
using DspAdpcm.Encode.Adpcm;
using DspAdpcm.Encode.Formats;
using DspAdpcm.Encode.Wave;

namespace DspAdpcm.Cli
{
    public static class DspAdpcmCli
    {
        public static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: dspenc <wavin> <dspout>\n");
                return 0;
            }

            IPcmStream wave;

            try
            {
                using (var file = new FileStream(args[0], FileMode.Open))
                {
                    wave = new WaveStream(file);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return -1;
            }

            Stopwatch watch = new Stopwatch();
            watch.Start();

            AdpcmStream adpcm = DspAdpcm.Encode.Adpcm.Encode.PcmToAdpcm(wave);

            watch.Stop();
            Console.WriteLine($"DONE! {adpcm.NumSamples} samples processed\n");
            Console.WriteLine($"Time elapsed: {watch.Elapsed.TotalSeconds}");
            Console.WriteLine($"Processed {(adpcm.NumSamples / watch.Elapsed.TotalMilliseconds):N} samples per milisecond.");

            var dsp = new Dsp(adpcm);

            using (var stream = File.Open(args[1], FileMode.Create))
                foreach (var b in dsp.GetFile())
                    stream.WriteByte(b);

            return 0;
        }
    }
}
