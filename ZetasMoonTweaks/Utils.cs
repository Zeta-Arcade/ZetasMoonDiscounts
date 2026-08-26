using System;

namespace ZetasMoonTweaks
{
    internal static class Utils
    {
        public static float SwapLetterForRiskFloat(char target)
        {
            float output = 0f;
            switch (target)
            {
                case 'X':
                    output = 10;
                    break;
                case 'S':
                    output = 6;
                    break;
                case 'A':
                    output = 5;
                    break;
                case 'B':
                    output = 4;
                    break;
                case 'C':
                    output = 3;
                    break;
                case 'D':
                    output = 2;
                    break;
                case 'E':
                    output = 1;
                    break;
                case 'F':
                    output = 0;
                    break;
                default:
                    output = 0;
                    break;
            }
            return output;
        }
        public static float SwapSymbolsForRiskFloat(string target)
        {
            float output = 0f;
            switch (target)
            {
                case "-":
                    output = 0.31f;
                    break;
                case "+":
                    output = 0.66f;
                    break;
                case "++":
                    output = 0.76f;
                    break;
                case "*":
                    output = 0.86f;
                    break;
                default:
                    output = 0.5f;
                    break;
            }
            return output;
        }
        public static string SwapIntForRiskLetter(int riskInt)
        {
            string output = "";
            if (riskInt > 10)
            {
                output = "X";
            }
            else if (riskInt > 6)
            {
                output = "S";
            }
            else
            {
                switch (riskInt)
                {
                    case 0:
                        output = "F";
                        break;
                    case 1:
                        output = "E";
                        break;
                    case 2:
                        output = "D";
                        break;
                    case 3:
                        output = "C";
                        break;
                    case 4:
                        output = "B";
                        break;
                    case 5:
                        output = "A";
                        break;
                    case 6:
                        output = "S";
                        break;
                    case 10:
                        output = "X";
                        break;
                    default:
                        output = "?";
                        break;
                }
            }
            return output;
        }
        public static string SwapFloatDecimalsForRiskSymbol(float riskFloat)
        {
            string output = "";
            // Take float, e.g. 9.76
            float isolatedDecimals = riskFloat - (int)riskFloat; // E.g. 9.76 - 9 = 0.76
            double isolatedDecRounded = Math.Round(isolatedDecimals, 2);
            if (isolatedDecRounded >= 0.85)
            {
                output = "*";
            }
            else if (isolatedDecRounded >= 0.75)
            {
                output = "++";
            }
            else if (isolatedDecRounded >= 0.65)
            {
                output = "+";
            }
            else if (isolatedDecRounded >= 0.3)
            {
                output = "";
            }
            else
            {
                output = "-";
            }
            return output;
        }
        public static float ConvertRiskLevelToFloat(string riskLevel)
        {
            float output = 0;
            float baseAmount = 0;
            float decimalAmount = 0;
            char[] characters = riskLevel.ToCharArray();
            baseAmount = SwapLetterForRiskFloat(characters[0]);
            string riskLevelSymbols = riskLevel.TrimStart(characters[0]);
            if (riskLevelSymbols.Length > 0)
            {
                decimalAmount = SwapSymbolsForRiskFloat(riskLevelSymbols);
            }
            else
            {
                decimalAmount = 0.5f;
            }
            ZMTBase.Logger.LogDebug($"Converted " + riskLevel + " to " + baseAmount + " + " + decimalAmount);
            output = baseAmount + decimalAmount;
            return output;
        }
        public static string ConvertFloatToRiskLevel(float floatRiskLevel)
        {
            string output = "";
            string part1 = SwapIntForRiskLetter((int)floatRiskLevel); //Doesn't round, just cuts off the decimal part
            string part2 = SwapFloatDecimalsForRiskSymbol(floatRiskLevel);
            output = part1 + part2;
            return output;
        }
    }
}
