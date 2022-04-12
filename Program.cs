using DeepSpeechClient;
using DeepSpeechClient.Models;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static System.Console;

namespace audioTranscribe
{
    internal class Program
    {

        static readonly Dictionary<string, string> alignments = new Dictionary<string, string>
        (
            new Dictionary<string, string>
            {
                ["TopLeft"]=@"{\an7}",
                ["TopCenter"] = @"{\an8}",
                ["TopRight"] = @"{\an9}",

                ["MiddleLeft"] = @"{\an4}",
                ["MiddleCenter"] = @"{\an5}",
                ["MiddleRight"] = @"{\an6}",

                ["BottomLeft"] = @"{\an1}",
                ["BottomCenter"] = "",
                ["BottomRight"] = @"{\an3}",
            },
            StringComparer.InvariantCultureIgnoreCase
        );

        static string alignment;

        static string TimeStr(float time)
        {
            var date = TimeSpan.FromSeconds(time);
            var str = date.ToString(@"hh\:mm\:ss\.fff");

            return str;
        }

        static IEnumerable<(float timef, float timee, string text)> splitWords(CandidateTranscript transcript)
        {
            float timef = 0;
            string text = null;

            foreach (var token in transcript.Tokens)
            {
                if (text==null)
                {
                    timef=token.StartTime;
                    text = token.Text;
                }
                else if (token.Text==" ")
                {
                    yield return (timef, token.StartTime, text);
                    text=null;
                }
                else
                    text += token.Text;
            }
            if (text!=null)
                yield return (timef, transcript.Tokens.Last().StartTime, text);
        }

        static IEnumerable<(float timef, float timee, string text)> splitLinesSubTitles(IEnumerable<(float timef, float timee, string text)> words)
        {
            var wordf = words.First();
            var timee = wordf.timee;
            var text = wordf.text;
            var npalabras = 0;

            foreach (var word in words.Skip(1))
            {
                var text2 = $"{text} {word.text}";
                var timee2 = word.timee;
                var timediff = timee2-wordf.timef;

                //  Cada línea no puede contener más de 35 caracteres
                if (text2.Length>35)
                    text=$"{text}\n{word.text}";

                //En cuanto a la limitación del tiempo, un subtítulo tiene una duración mínima de un segundo y una duración máxima de seis segundos en pantalla.
                // Se estima que la velocidad de lectura media actual es de tres palabras por segundo.
                if (timediff>6.0 || timediff>3.0f*(((npalabras+1)/3)+1))
                {
                    yield return (wordf.timef, timee, text);

                    wordf=word;
                    timee = wordf.timee;
                    text = wordf.text;
                    npalabras=0;
                }
                else
                {
                    text=text2;
                    timee =timee2;
                    npalabras++;
                }
            }

            yield return (wordf.timef, timee, text);
        }

        static async Task writeSrt(TextWriter writer, IEnumerable<(float timef, float timee, string text)> linesSrt)
        {
            var i = 1;

            foreach (var line in linesSrt)
            {
                if (i>1)
                    await writer.WriteLineAsync();

                await writer.WriteLineAsync($"{i++}");
                await writer.WriteLineAsync($"{TimeStr(line.timef)} --> {TimeStr(line.timee)}");
                await writer.WriteLineAsync($"{alignment}{line.text}");
            }
        }

        static async Task audioTranscribe(WaveBuffer buffer, string filesrt)
        {
            CandidateTranscript transcript;

            Write("\nTranscribiendo ...");
            using (var deepSpeechClient = new DeepSpeech("deepspeech-0.9.3-models.pbmm"))
            {
                var metadata = await Task.Run(() => deepSpeechClient.SpeechToTextWithMetadata(buffer.ShortBuffer, Convert.ToUInt32(buffer.MaxSize / 2), 1));
                transcript=metadata.Transcripts[0];
            }

            //Write(string.Join("", transcript.Tokens.Select(t => t.Text)));

            Write("\nCreando fichero subtitulos ...");

            var words = splitWords(transcript);
            var linesSrt = splitLinesSubTitles(words);

            using (var writer = new StreamWriter(filesrt))
            {
                await writeSrt(writer, linesSrt);
            }
        }

        static async Task audioTranscribe(string fileaudio, string filesrt)
        {
            var waveBuffer = new WaveBuffer(File.ReadAllBytes(fileaudio));

            await audioTranscribe(waveBuffer, filesrt);
        }

        static void exportAudio(string filevideo, out string fileaudio)
        {
            fileaudio =Path.ChangeExtension(filevideo, ".wav");

            Write("exportando a audio ...");
            Process.Start("ffmpeg.exe", $"-i \"{filevideo}\" -y -ar 16000 -ac 1 \"{fileaudio}\"").WaitForExit();
        }

        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                var astr = string.Join(",", alignments.Keys);

                WriteLine($"audiTranscribe <file video> [alignment]\nalignment = {astr}");

                return -1;
            }

            var filevideo = args[0];
            var filesrt = Path.ChangeExtension(filevideo, ".srt");

            if (args.Length==2)
            {
                if (!alignments.TryGetValue(args[1], out alignment))
                {
                    WriteLine($"alignment '{args[1]} is incorrect");

                    return -1;
                }
            }
            else
                alignment="";

            WriteLine($"Fichero de video a subtitular : {filevideo}");

            try
            {
                var stopwatch = Stopwatch.StartNew();

                exportAudio(filevideo, out string fileaudio);

                try
                {
                    audioTranscribe(fileaudio, filesrt).ConfigureAwait(false).GetAwaiter().GetResult();
                }
                finally
                {
                    try
                    {
                        File.Delete(fileaudio);
                    }
                    catch { }
                }

                stopwatch.Stop();
                WriteLine($"\ncreado fichero subtitulo : {filesrt}.\nTrancripción realizada en {stopwatch.Elapsed}");
            }
            catch (Exception ex)
            {
                WriteLine($"\nException : {ex.Message}");
            }

            return 0;
        }
    }
}
