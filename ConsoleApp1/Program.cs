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

namespace OCR_test
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var path = Assembly.GetExecutingAssembly().Location;
            var tessPath = Path.Combine(Path.GetDirectoryName(path), "TessData");
            byte[] imgData;
            using (var file = File.OpenRead("image3.jpg"))
            {
                imgData = new byte[file.Length];
                file.Read(imgData, 0, imgData.Length);
            }

            var res =  await Recognition(tessPath, imgData);
            using var sw = new StreamWriter("output.txt", false);
            for (int i = 0; i < res.Count; i++)
            {
                sw.Write($"{res[i].Item1}: {res[i].Item2}");
            }
            sw.Flush();
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

        
        static async Task<(byte[], byte[], byte[])>   ProcessImage(byte[] imageBytes)
        {
            // 1. Загрузка изображения
            using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);

            Mat transformer = await ExtractAndWarpScreenAsync(src);

            File.WriteAllBytes("TRANSFORMER.jpg", transformer.ToBytes(".jpg"));

            // 2. Улучшение контраста (CLAHE) только для яркости
            using var claheImg = new Mat();
            using (var clahe = Cv2.CreateCLAHE(clipLimit: 3.0, tileGridSize: new OpenCvSharp.Size(8, 8)))
            {
                using var lab = new Mat();
                Cv2.CvtColor(transformer, lab, ColorConversionCodes.BGR2Lab);

                var channels = Cv2.Split(lab);
                using var lChannel = channels[0]; // Канал яркости
                using var aChannel = channels[1];
                using var bChannel = channels[2];




                clahe.Apply(lChannel, lChannel);

                using var newLab = new Mat();
                Cv2.Merge(new[] { lChannel, aChannel, bChannel }, newLab);
                Cv2.CvtColor(newLab, claheImg, ColorConversionCodes.Lab2BGR);
            }

            File.WriteAllBytes("CLAHE.jpg", claheImg.ToBytes(".jpg"));


            // 3. Размытие и перевод в серый цвет
            using var blurred = new Mat();
            Cv2.GaussianBlur(claheImg, blurred, new OpenCvSharp.Size(5, 5), 2.0);
            //Cv2.MedianBlur(claheImg, blurred, 577);
            //Cv2.BilateralFilter(claheImg, blurred,15,150,150);

            File.WriteAllBytes("BLURRED.jpg", blurred.ToBytes(".jpg"));

            using var gray = new Mat();
            Cv2.CvtColor(blurred, gray, ColorConversionCodes.BGR2GRAY);



            File.WriteAllBytes("GRAYED.jpg", gray.ToBytes(".jpg"));









            /*
            using var normalized = new Mat();
            var kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new OpenCvSharp.Size(101, 101));

            Cv2.MorphologyEx(
                gray,
                background,
                MorphTypes.Close,
                kernel
             );
            Cv2.Absdiff(gray, background, normalized);
            */
            // Получаем карту освещения

            var bytes1 = gray.ToBytes(".jpg");

            using var background = new Mat();

        Cv2.GaussianBlur(
            gray,
            background,
            new OpenCvSharp.Size(0, 0),
            50
        );
        // Вычитаем фон
        using var diffed = new Mat();
        Cv2.Absdiff(gray, background, diffed);

            File.WriteAllBytes("ShaDOWcORRECT.jpg", diffed.ToBytes(".jpg"));

            //    using var normalized = new Mat();
            //Cv2.Normalize(diffed, normalized, 0, 255, NormTypes.MinMax);









            // 4. Ищем ЧЕРНЫЕ буквы (все, что темнее 51, станет черным)
            using var blackTextMask = new Mat();
        Cv2.Threshold(diffed, blackTextMask, 51, 255, ThresholdTypes.Binary);
            var bytes2 = diffed.ToBytes(".jpg");

            File.WriteAllBytes("BLACKtEXTmASK.jpg", blackTextMask.ToBytes(".jpg"));

            // 5. Ищем БЕЛЫЕ буквы (все, что светлее 204, станет черным после инверсии)
            using var whiteTextMask = new Mat();
        Cv2.Threshold(gray, whiteTextMask, 204, 255, ThresholdTypes.Binary);

            File.WriteAllBytes("wHITEtEXTmASK.jpg", whiteTextMask.ToBytes(".jpg"));

            // 6. Объединяем буквы вместе (Черные буквы + Белые буквы)
            using var allTextMask = new Mat();
        Cv2.BitwiseOr(blackTextMask, whiteTextMask, allTextMask);

            File.WriteAllBytes("aLLtEXTmASK.jpg", allTextMask.ToBytes(".jpg"));

            // 7. Создаем финальный результат: черные буквы на белом фоне
            using var result = new Mat(transformer.Size(), MatType.CV_8UC1, new Scalar(255)); // Белый лист

            File.WriteAllBytes("RESULTbefore.jpg", result.ToBytes(".jpg"));

            // Там, где был текст (allTextMask), красим в черный цвет (0)
            result.SetTo(new Scalar(0), allTextMask);

            File.WriteAllBytes("RESULTafter.jpg", result.ToBytes(".jpg"));

            var bytes3 = result.ToBytes(".jpg");

        return (bytes1, bytes2, bytes3);

        }
    


        public static async Task<List<(Tesseract.Rect, string)>> Recognition(string TessDataPath, byte[] imgData)
        {
            //string imagePath = "screen.jpg"; // Путь к фото
            //string tessData = @"C:\tessdata\"; // Путь к данным Tesseract (скачать tessdata)

            var corrImgData = await ProcessImage(imgData);
            //File.WriteAllBytes("postcorrection.jpg", corrImgData.Item1);
            //File.WriteAllBytes("postcorrectionN.jpg", corrImgData.Item2);
            //File.WriteAllBytes("postcorrectionSum.jpg", corrImgData.Item3);

            //var corrImgDataN = ProcessImageNeg(imgData);
            //File.WriteAllBytes("postcorrectionN.jpg", corrImgDataN);

            List<(Tesseract.Rect, string)> result = new System.Collections.Generic.List<(Tesseract.Rect, string)>();
            using (var engine = new TesseractEngine(TessDataPath, "rus+eng", EngineMode.LstmOnly))
            using (var img = Pix.LoadFromMemory(corrImgData.Item3))
            using (var page = engine.Process(img))
            {
                using (var iter = page.GetIterator())
                {
                    iter.Begin();
                    do
                    {
                        if (iter.IsAtBeginningOf(PageIteratorLevel.TextLine))
                        {
                            var text = iter.GetText(PageIteratorLevel.TextLine);
                            if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out Tesseract.Rect bbox))
                            {
                                result.Add((bbox, text));
                            }
                        }
                    } while (iter.Next(PageIteratorLevel.TextLine));
                }
            }

            return result;
        }

        // HttpClient переиспользуется, чтобы не плодить сокеты
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // Структура для десериализации точек из ответа Qwen
        private class ScreenCoordinates
        {
            public float[] top_left { get; set; }
            public float[] top_right { get; set; }
            public float[] bottom_right { get; set; }
            public float[] bottom_left { get; set; }
        }

        /// <summary>
        /// Принимает оригинальный Mat, находит рамку экрана через Qwen2.5-VL,
        /// исправляет перспективу и возвращает выровненный Mat.
        /// </summary>
        public static async Task<Mat> ExtractAndWarpScreenAsync(Mat src)
        {
            if (src == null || src.Empty())
                throw new ArgumentException("Исходный Mat пуст или не инициализирован.");

            // === 1. УМЕНЬШЕНИЕ МАСШТАБА ДЛЯ OLLAMA СРЕДСТВАМИ OPENCV ===
            const int maxDimension = 1280;
            Mat resized = new Mat();

            if (src.Width > maxDimension || src.Height > maxDimension)
            {
                double scale = maxDimension / (double)Math.Max(src.Width, src.Height);
                int newWidth = (int)(src.Width * scale);
                int newHeight = (int)(src.Height * scale);
                // Используем Area или Linear интерполяцию для сохранения четкости текста
                Cv2.Resize(src, resized, new OpenCvSharp.Size(newWidth, newHeight), 0, 0, InterpolationFlags.Area);
            }
            else
            {
                resized = src.Clone(); // Если картинка и так маленькая, просто копируем
            }

            // === 2. КОДИРОВАНИЕ В BASE64 ===
            // Кодируем ужатый Mat в JPEG прямо в памяти
            Cv2.ImEncode(".jpg", resized, out byte[] imageBytes);
            string b64String = Convert.ToBase64String(imageBytes);
            resized.Dispose(); // Освобождаем память от временного пожатого кадра

            // === 3. ОТПРАВКА ЗАПРОСА В OLLAMA ===
            var payloadObject = new
            {
                model = "qwen2.5vl:3b",
                messages = new[]
                {
                new
                {
                    role = "user",
                    content = "Это технический монитор. Найди 4 угла САМОЙ ВНУТРЕННЕЙ рабочей области экрана (матрицы с текстом), " +
                              "игнорируя внешнюю пластиковую рамку (безель). " +
                              "Верни ответ СТРОГО в формате JSON с нормализованными координатами от 0 до 1000:\n" +
                              "{\n" +
                              "  \"top_left\": [x, y],\n" +
                              "  \"top_right\": [x, y],\n" +
                              "  \"bottom_right\": [x, y],\n" +
                              "  \"bottom_left\": [x, y]\n" +
                              "}",
                    images = new[] { b64String }
                }
            },
                stream = false,
                options = new { temperature = 0.0, top_p = 0.1 }
            };

            string jsonPayload = JsonSerializer.Serialize(payloadObject);
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("http://localhost:11434/api/chat", content);
            response.EnsureSuccessStatusCode();

            string responseString = await response.Content.ReadAsStringAsync();

            // === 4. ПАРСИНГ КООРДИНАТ ИЗ ОТВЕТА ===
            using JsonDocument doc = JsonDocument.Parse(responseString);
            string rawContent = doc.RootElement.GetProperty("message").GetProperty("content").GetString();

            Console.WriteLine($"\n[ОТВЕТ QWEN]:\n{rawContent}\n");

            // Извлекаем чистый JSON, если модель обернула его в маркдаун-блок ```json ... ```
            var jsonMatch = Regex.Match(rawContent, @"\{.*\}", RegexOptions.Singleline);
            if (!jsonMatch.Success)
                throw new Exception($"Модель вернула некорректный ответ без JSON структуры: {rawContent}");

            var coords = JsonSerializer.Deserialize<ScreenCoordinates>(jsonMatch.Value);

            // === 5. ПЕРЕСЧЕТ В РЕАЛЬНЫЕ ПИКСЕЛИ ОРИГИНАЛА ===
            int imgWidth = src.Width;
            int imgHeight = src.Height;

            Point2f[] srcPoints = new Point2f[]
            {
            new Point2f(coords.top_left[0] * imgWidth / 1000f,     coords.top_left[1] * imgHeight / 1000f),
            new Point2f(coords.top_right[0] * imgWidth / 1000f,    coords.top_right[1] * imgHeight / 1000f),
            new Point2f(coords.bottom_right[0] * imgWidth / 1000f, coords.bottom_right[1] * imgHeight / 1000f),
            new Point2f(coords.bottom_left[0] * imgWidth / 1000f,  coords.bottom_left[1] * imgHeight / 1000f)
            };

            // === 6. РАСЧЕТ РАЗМЕРОВ ЦЕЛЕВОГО ОКНА ===
            double widthTop = Point2f.Distance(srcPoints[0], srcPoints[1]);
            double widthBottom = Point2f.Distance(srcPoints[2], srcPoints[3]);
            int maxWidth = Convert.ToInt32(Math.Max(widthTop, widthBottom));

            double heightLeft = Point2f.Distance(srcPoints[0], srcPoints[3]);
            double heightRight = Point2f.Distance(srcPoints[1], srcPoints[2]);
            int maxHeight = Convert.ToInt32(Math.Max(heightLeft, heightRight));

            Point2f[] destPoints = new Point2f[]
            {
            new Point2f(0, 0),
            new Point2f(maxWidth - 1, 0),
            new Point2f(maxWidth - 1, maxHeight - 1),
            new Point2f(0, maxHeight - 1)
            };

            // === 7. ВОССТАНОВЛЕНИЕ ПЕРСПЕКТИВЫ (WARP) ===
            using Mat transformMatrix = Cv2.GetPerspectiveTransform(srcPoints, destPoints);

            Mat dst = new Mat();
            // Применяем трансформацию прямо к оригинальному (качественному) Mat
            Cv2.WarpPerspective(src, dst, transformMatrix, new OpenCvSharp.Size(maxWidth, maxHeight), InterpolationFlags.Cubic);

            return dst; // Возвращаем идеально ровный вырезанный экран
        }

    }
}