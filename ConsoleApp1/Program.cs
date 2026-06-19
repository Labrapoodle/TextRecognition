using OpenCvSharp;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tesseract;
using System.Diagnostics; 
using System.Net.Http;

namespace OCR_test
{
    internal class Program
    {
        private static readonly HttpClient _httpClient =  new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        static async Task Main()
        {
            string takesDir =
                @"C:\Users\k_alejnikov\Pictures\takes";

            string outputDir =
                @"C:\Users\k_alejnikov\Pictures";

            string[] files =
                Directory.GetFiles(takesDir)
                         .Take(2)
                         .ToArray();

            for (int i = 0; i < files.Length; i++)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"===== IMAGE {i + 1} =====");

                Stopwatch swTotal =
                    Stopwatch.StartNew();

                byte[] bytes =
                    await File.ReadAllBytesAsync(files[i]);

                Stopwatch swQwen =
                    Stopwatch.StartNew();

                List<TextBox> boxes =
                    await DetectTextBlocks(bytes);

                swQwen.Stop();

                using var img =
                    Cv2.ImDecode(
                        bytes,
                        ImreadModes.Color);

                Stopwatch swCv =
                    Stopwatch.StartNew();

                using var result =
                    KeepOnlyTextAreas(img, boxes);

                swCv.Stop();

                string outFile =
                    Path.Combine(
                        outputDir,
                        $"result_{i + 1}.png");

                Cv2.ImWrite(outFile, result);

                swTotal.Stop();

                Console.WriteLine(
                    $"Boxes found: {boxes.Count}");

                Console.WriteLine(
                    $"Qwen: {swQwen.ElapsedMilliseconds} ms");

                Console.WriteLine(
                    $"OpenCV: {swCv.ElapsedMilliseconds} ms");

                Console.WriteLine(
                    $"Total: {swTotal.ElapsedMilliseconds} ms");

                Console.WriteLine(
                    $"Saved: {outFile}");
            }
        }

        /*
        static (byte[], byte[], byte[]) ProcessImage(byte[] imageBytes)
        {
            using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);

            // 1. CLAHE — умеренно, чтобы не усилить шум
            using var claheImg = new Mat();
            using (var clahe = Cv2.CreateCLAHE(clipLimit: 1.5,
                                                tileGridSize: new OpenCvSharp.Size(8, 8)))
            {
                using var lab = new Mat();
                Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);
                var channels = Cv2.Split(lab);
                clahe.Apply(channels[0], channels[0]);
                using var newLab = new Mat();
                Cv2.Merge(channels, newLab);
                Cv2.CvtColor(newLab, claheImg, ColorConversionCodes.Lab2BGR);
                foreach (var ch in channels) ch.Dispose();
            }

            // 2. Перевод в серый
            using var gray = new Mat();
            Cv2.CvtColor(claheImg, gray, ColorConversionCodes.BGR2GRAY);

            // 3. КЛЮЧЕВОЙ ШАГ: удаление шума БЕЗ размытия букв
            // fastNlMeansDenoising убирает зерно, сохраняя края букв
            // h=10 — сила денойза; templateWindowSize=7; searchWindowSize=21
            using var denoised = new Mat();
            Cv2.FastNlMeansDenoising(gray, denoised, h: 6,
                                      templateWindowSize: 7,
                                      searchWindowSize: 21);

            // 4. Маска бликов (до порогов, чтобы исключить засветки)
            using var glare = new Mat();
            Cv2.Threshold(denoised, glare, 230, 255, ThresholdTypes.Binary);
            using var glareKernel = Cv2.GetStructuringElement(
                MorphShapes.Ellipse, new OpenCvSharp.Size(11, 11));
            using var glareDilated = new Mat();
            Cv2.Dilate(glare, glareDilated, glareKernel);

            // 5. Адаптивный порог для ЧЁРНОГО текста
            // После денойза blockSize можно сделать меньше — меньше ореолов
            using var blackTextMask = new Mat();
            Cv2.AdaptiveThreshold(
                denoised,
                blackTextMask,
                maxValue: 255,
                adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
                thresholdType: ThresholdTypes.BinaryInv,
                blockSize: 15,   // ← уменьшили с 31 до 15
                c: 8
            );

            // 6. Маска БЕЛОГО текста через локальный контраст
            using var localMin = new Mat();
            using var dilKernel = Cv2.GetStructuringElement(
                MorphShapes.Rect, new OpenCvSharp.Size(15, 15));
            Cv2.Erode(denoised, localMin, dilKernel);
            using var localContrast = new Mat();
            Cv2.Subtract(denoised, localMin, localContrast);
            using var whiteTextMask = new Mat();
            Cv2.Threshold(localContrast, whiteTextMask, 35, 255, ThresholdTypes.Binary);
            // Белый текст должен быть сам по себе достаточно ярким
            using var brightEnough = new Mat();
            Cv2.Threshold(denoised, brightEnough, 120, 255, ThresholdTypes.Binary);
            Cv2.BitwiseAnd(whiteTextMask, brightEnough, whiteTextMask);

            // 7. Объединение масок
            using var allTextMask = new Mat();
            Cv2.BitwiseOr(blackTextMask, whiteTextMask, allTextMask);

            // 8. Исключаем блики
            using var glareInv = new Mat();
            Cv2.BitwiseNot(glareDilated, glareInv);
            Cv2.BitwiseAnd(allTextMask, glareInv, allTextMask);

            // 9. Морфологическое открытие — убираем точечный шум
            // Размер ядра 2×2: убивает одиночные пиксели, буквы не трогает
            using var openKernel = Cv2.GetStructuringElement(
                MorphShapes.Rect, new OpenCvSharp.Size(2, 2));
            using var cleaned = new Mat();
            Cv2.MorphologyEx(allTextMask, cleaned, MorphTypes.Open, openKernel);

            // 10. Финальный результат
            using var result = new Mat(src.Size(), MatType.CV_8UC1, new Scalar(255));
            result.SetTo(new Scalar(0), cleaned);

            return (
                glareDilated.ToBytes(".png"),  // debug: маска бликов
                whiteTextMask.ToBytes(".png"), // debug: маска белого текста
                result.ToBytes(".png")         // результат
            );
        }
        */


        static Mat KeepOnlyTextAreas(
    Mat source,
    List<TextBox> boxes)
        {
            Mat result =
                new Mat(
                    source.Size(),
                    source.Type(),
                    Scalar.White);

            foreach (var box in boxes)
            {
                int x1 =
                    (int)(box.x1 * source.Width / 1000f);

                int y1 =
                    (int)(box.y1 * source.Height / 1000f);

                int x2 =
                    (int)(box.x2 * source.Width / 1000f);

                int y2 =
                    (int)(box.y2 * source.Height / 1000f);

                x1 = Math.Max(0, x1);
                y1 = Math.Max(0, y1);

                x2 = Math.Min(source.Width - 1, x2);
                y2 = Math.Min(source.Height - 1, y2);
                /*
               var rect =
                   new OpenCvSharp.Rect(
                       x1,
                       y1,
                       x2 - x1,
                       y2 - y1);

               using var srcRoi =
                   new Mat(source, rect);

               using var dstRoi =
                   new Mat(result, rect);

               srcRoi.CopyTo(dstRoi);
                */

                Cv2.Rectangle(
                source,
                new OpenCvSharp.Point(x1, y1),
                new OpenCvSharp.Point(x2, y2),
                Scalar.Red,
                5);
            }

            return result;
        }

        static async Task<List<TextBox>> DetectTextBlocks(byte[] imageBytes)
        {

            using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);

            const int maxDimension = 1280;

            using var resized = new Mat();

            if (src.Width > maxDimension || src.Height > maxDimension)
            {
                double scale =
                    maxDimension /
                    (double)Math.Max(src.Width, src.Height);

                Cv2.Resize(
                    src,
                    resized,
                    new OpenCvSharp.Size(
                        (int)(src.Width * scale),
                        (int)(src.Height * scale)),
                    0,
                    0,
                    InterpolationFlags.Area);
            }
            else
            {
                src.CopyTo(resized);
            }


            Mat gray = new();
            Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.ImEncode(".jpg", gray, out byte[] smallImage);

            string b64 = Convert.ToBase64String(smallImage);

            var payload = new
            {
                model = "qwen2.5vl:3b",                
                messages = new[]
                {
                new
                {
                    role = "user",
                    content =
                   """
                   На фотографии промышленный экран управления.

                   Найди ВСЕ области, содержащие любую текстовую информацию:

                   - русский текст;
                   - английский текст;
                   - буквенно-цифровые коды;
                   - параметры вида P21, T9030, N9000;
                   - числа;
                   - единицы измерения (bar, mm, %, l/min и т.д.);
                   - строки состояния;
                   - сообщения об ошибках;
                   - меню и заголовки.

                   Не объединяй удалённые области между собой.

                   Верни только JSON массив.

                   Координаты нормализованы от 0 до 1000.

                   Формат:

                   [
                     {
                       "x1": 100,
                       "y1": 100,
                       "x2": 300,
                       "y2": 150
                     }
                   ]

                   Без markdown.
                   Без комментариев.
                   Без пояснений.
                   """,
                    images = new[] { b64 }
                }
            },
                stream = false,
                options = new
                {
                    temperature = 0
                }
            };

            string json = JsonSerializer.Serialize(payload);

            using var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            var response =
                await _httpClient.PostAsync(
                    "http://localhost:11434/api/chat",
                    content);

            
            

            string responseString =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine();
                Console.WriteLine("=== OLLAMA ERROR ===");
                Console.WriteLine(response.StatusCode);
                Console.WriteLine(responseString);
                Console.WriteLine("====================");
                Console.WriteLine();

                throw new Exception(
                    $"Ollama returned {(int)response.StatusCode}");
            }

            using JsonDocument doc =
                JsonDocument.Parse(responseString);

            string raw =
                doc.RootElement
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

            raw = raw.Replace("```json", "");
            raw = raw.Replace("```", "");
            raw = raw.Trim();

            var match =
                Regex.Match(raw, @"\[.*\]",
                RegexOptions.Singleline);

            if (!match.Success)
                throw new Exception(raw);

            return JsonSerializer.Deserialize<List<TextBox>>(match.Value)!;
        }

        public sealed class TextBox
        {
            public float x1 { get; set; }
            public float y1 { get; set; }
            public float x2 { get; set; }
            public float y2 { get; set; }
        }

    }
}