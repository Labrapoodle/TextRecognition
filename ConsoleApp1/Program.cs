using OpenCvSharp;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using Tesseract;

namespace OCR_test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var path = Assembly.GetExecutingAssembly().Location;
            var tessPath = Path.Combine(Path.GetDirectoryName(path), "TessData");
            byte[] imgData;
            using (var file = File.OpenRead("image.jpg"))
            {
                imgData = new byte[file.Length];
                file.Read(imgData, 0, imgData.Length);
            }

            var res = Recognition(tessPath, imgData);
            using var sw = new StreamWriter("output.txt", false);
            for (int i = 0; i < res.Count; i++)
            {
                sw.Write($"{res[i].Item1}: {res[i].Item2}");
            }
            sw.Flush();
        }


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


        /*
        static (byte[], byte[], byte[]) ProcessImage(byte[] imageBytes)
        {
            // 1. Загрузка изображения
            using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);

            // 2. Улучшение контраста (CLAHE) только для яркости
            using var claheImg = new Mat();
            using (var clahe = Cv2.CreateCLAHE(clipLimit: 3.0, tileGridSize: new OpenCvSharp.Size(8, 8)))
            {
                using var lab = new Mat();
                Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);

                var channels = Cv2.Split(lab);
                using var lChannel = channels[0]; // Канал яркости
                using var aChannel = channels[1];
                using var bChannel = channels[2];

                clahe.Apply(lChannel, lChannel);

                using var newLab = new Mat();
                Cv2.Merge(new[] { lChannel, aChannel, bChannel }, newLab);
                Cv2.CvtColor(newLab, claheImg, ColorConversionCodes.Lab2BGR);
            }

            // 3. Размытие и перевод в серый цвет
            using var blurred = new Mat();
            Cv2.GaussianBlur(claheImg, blurred, new OpenCvSharp.Size(5, 5), 2.0);
            //Cv2.MedianBlur(claheImg, blurred, 577);
            
            using var gray = new Mat();
            Cv2.CvtColor(blurred, gray, ColorConversionCodes.BGR2GRAY);


        */










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


        /*
        using var background = new Mat();

        Cv2.GaussianBlur(
            gray,
            background,
            new OpenCvSharp.Size(0, 0),
            50
        );
        // Вычитаем фон
        using var normalized = new Mat();
        Cv2.Absdiff(gray, background, normalized);

        // 4. Ищем ЧЕРНЫЕ буквы (все, что темнее 51, станет черным)
        using var blackTextMask = new Mat();
        Cv2.Threshold(normalized, blackTextMask, 51, 255, ThresholdTypes.Binary);
        var bytes1 = blackTextMask.ToBytes(".jpg");

        // 5. Ищем БЕЛЫЕ буквы (все, что светлее 204, станет черным после инверсии)
        using var whiteTextMask = new Mat();
        Cv2.Threshold(gray, whiteTextMask, 204, 255, ThresholdTypes.Binary);
        var bytes2 = whiteTextMask.ToBytes(".jpg");

        // 6. Объединяем буквы вместе (Черные буквы + Белые буквы)
        using var allTextMask = new Mat();
        Cv2.BitwiseOr(blackTextMask, whiteTextMask, allTextMask);

        // 7. Создаем финальный результат: черные буквы на белом фоне
        using var result = new Mat(src.Size(), MatType.CV_8UC1, new Scalar(255)); // Белый лист

        // Там, где был текст (allTextMask), красим в черный цвет (0)
        result.SetTo(new Scalar(0), allTextMask);

        var bytes3 = result.ToBytes(".jpg");

        return (bytes1, bytes2, bytes3);

    }
    */


        public static List<(Tesseract.Rect, string)> Recognition(string TessDataPath, byte[] imgData)
        {
            //string imagePath = "screen.jpg"; // Путь к фото
            //string tessData = @"C:\tessdata\"; // Путь к данным Tesseract (скачать tessdata)

            var corrImgData = ProcessImage(imgData);
            File.WriteAllBytes("postcorrection.png", corrImgData.Item1);
            File.WriteAllBytes("postcorrectionN.png", corrImgData.Item2);
            File.WriteAllBytes("postcorrectionSum.png", corrImgData.Item3);

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


    }
}