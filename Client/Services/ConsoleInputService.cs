using System;

// Содержит общие методы безопасного ввода из консоли.
public class ConsoleInputService
{
    // Считывает номер пункта меню в заданном диапазоне.
    public int ReadMenuNumber(int min, int max, string message)
    {
        while (true)
        {
            Console.Write(message);
            string input = Console.ReadLine() ?? "";

            if (int.TryParse(input, out int selectedNumber) && selectedNumber >= min && selectedNumber <= max)
            {
                Console.WriteLine();
                return selectedNumber;
            }

            Console.WriteLine("Некорректный ввод.");
        }
    }

    // Считывает ответ y/n и поддерживает русские варианты да/нет.
    public bool AskYesNo(string message)
    {
        while (true)
        {
            Console.Write(message);
            string answer = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();

            if (answer == "Y" || answer == "YES" || answer == "Д" || answer == "ДА")
            {
                Console.WriteLine();
                return true;
            }

            if (answer == "N" || answer == "NO" || answer == "Н" || answer == "НЕТ")
            {
                Console.WriteLine();
                return false;
            }

            Console.WriteLine("Некорректный ввод. Введите y или n.");
        }
    }

    // Считывает строку или возвращает значение по умолчанию при пустом вводе.
    public string ReadStringWithDefault(string message, string defaultValue)
    {
        Console.Write(message);
        string input = Console.ReadLine() ?? "";
        return string.IsNullOrWhiteSpace(input) ? defaultValue : input;
    }

    // Считывает целое число или возвращает значение по умолчанию при пустом вводе.
    public int ReadIntWithDefault(string message, int defaultValue)
    {
        while (true)
        {
            Console.Write(message);
            string input = Console.ReadLine() ?? "";

            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;

            if (int.TryParse(input, out int result))
                return result;

            Console.WriteLine("Некорректное число. Попробуйте снова.");
        }
    }

    // Позволяет выбрать путь из списка: 0 отменяет выбор, остальные пункты начинаются с 1.
    public string SelectPathFromList(string[] files, string prompt)
    {
        if (files.Length == 0)
        {
            Console.WriteLine();
            return "";
        }

        while (true)
        {
            Console.Write(prompt);
            string input = Console.ReadLine() ?? "";

            if (int.TryParse(input, out int selectedNumber))
            {
                if (selectedNumber == 0)
                {
                    Console.WriteLine();
                    return "";
                }

                if (selectedNumber >= 1 && selectedNumber <= files.Length)
                {
                    Console.WriteLine();
                    return files[selectedNumber - 1];
                }
            }

            Console.WriteLine("Некорректный ввод.");
        }
    }
}
