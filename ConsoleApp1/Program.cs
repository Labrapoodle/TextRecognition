using OpenCvSharp;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Tesseract;
using System.Text.RegularExpressions;

namespace OCR_test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // 1. Загружаем изображение СРАЗУ в черно-белом (Grayscale) формате
            // Флаг ImreadModes.Grayscale гарантирует, что мы получим один канал на пиксель
            using Mat srcGray = Cv2.ImRead("image.jpg", ImreadModes.Grayscale);

            if (srcGray.Empty())
            {
                Console.WriteLine("Не удалось загрузить изображение!");
                return;
            }

            int h = srcGray.Rows;
            int w = srcGray.Cols;

            // 2. Создаем массив byte[,] и переносим в него данные из Mat
            byte[,] inputData = new byte[h, w];

            // Используем эффективный индексатор OpenCvSharp для безопасного чтения пикселей
            var indexerIn = srcGray.GetGenericIndexer<byte>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    inputData[y, x] = indexerIn[y, x];
                }
            }

            // 3. Передаем массив в ваш метод локального сигмоидального контраста
            int radius = 15;    // Радиус локального окна анализа (чем больше, тем крупнее детали)
            double k = 10.0;    // Коэффициент крутизны сигмоиды (сила контраста)

            Console.WriteLine("Запуск локального изменения контраста... Это может занять некоторое время.");
            byte[,] resultData = LocalSigmoidContrast(inputData, radius, k);

            // 4. Переносим результат из byte[,] обратно в структуру OpenCV Mat
            using Mat resultMat = new Mat(h, w, MatType.CV_8UC1);
            var indexerOut = resultMat.GetGenericIndexer<byte>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    indexerOut[y, x] = resultData[y, x];
                }
            }

            // 5. Сохраняем готовую фотографию на диск
            Cv2.ImWrite("SIGMOID_RESULT.jpg", resultMat);
            Console.WriteLine("Изображение успешно обработано и сохранено как 'SIGMOID_RESULT.jpg'!");
        }

        public static byte[,] LocalSigmoidContrast(
    byte[,] img,
    int radius,
    double k)
        {
            int h = img.GetLength(0);
            int w = img.GetLength(1);

            byte[,] result = new byte[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int min = 255;
                    int max = 0;

                    for (int yy = Math.Max(0, y - radius);
                         yy <= Math.Min(h - 1, y + radius);
                         yy++)
                    {
                        for (int xx = Math.Max(0, x - radius);
                             xx <= Math.Min(w - 1, x + radius);
                             xx++)
                        {
                            byte v = img[yy, xx];

                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                    }

                    if (max == min)
                    {
                        result[y, x] = img[y, x];
                        continue;
                    }

                    double xNorm =
                        (img[y, x] - min) /
                        (double)(max - min);

                    double ySig =
                        1.0 /
                        (1.0 + Math.Exp(-k * (xNorm - 0.5)));

                    result[y, x] =
                        (byte)(255.0 * ySig);
                }
            }

            return result;
        }



    }
}