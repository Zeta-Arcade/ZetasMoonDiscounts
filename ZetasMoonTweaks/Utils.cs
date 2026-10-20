using Dawn;
using Dawn.Internal;
using Dawn.Utils;
using LethalLevelLoader;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Reflection;
using DunGen.Graph;

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
            float decimals = riskFloat - (float)Math.Floor(riskFloat);
            if (decimals >= 0.81f) return "*"; //Should be in the middle now
            if (decimals >= 0.71f) return "++";
            if (decimals >= 0.58f) return "+";
            if (decimals >= 0.405f) return "";
            return "-";
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
        public static string CleanInteriorName(string interiorName)
        {
            //Get rid of Clones, Dungeons and Flow
            interiorName = interiorName.Replace("(Clone)", "").Replace("DungeonFlow", "").Replace("Flow", "").Replace("Dungeon", "").Trim();
            // If words are in PascalCase then seperate them
            interiorName = Regex.Replace(interiorName, @"([a-z])([A-Z])", "$1 $2");
            // Remove spaces
            interiorName = Regex.Replace(interiorName, @"\s+", " ").Trim();
            return interiorName;
        }
        public static string? GetCurrentInteriorName()
        {
            var dungeonGenerator = RoundManager.Instance?.dungeonGenerator;
            if (dungeonGenerator == null) return null;
            return dungeonGenerator.Generator?.DungeonFlow?.name;
        }
        public static DawnDungeonInfo? GetCurrentDungeonInfo()
        {
            var dungeon = RoundManager.Instance?.dungeonGenerator?.Generator?.DungeonFlow;
            if (dungeon == null) return null;
            return dungeon.GetDawnInfo();
        }
        public static float GetInteriorChance(DawnDungeonInfo? selectedDungeon)
        {
            if (selectedDungeon == null) return -1f;
            var currentLevel = StartOfRound.Instance.currentLevel;
            if (currentLevel == null) return -1f;
            var moonInfo = LethalContent.Moons.Values.FirstOrDefault(m => m.Level == currentLevel);
            if (moonInfo == null) return -1f;
            var weatherEffect = TimeOfDayRefs.GetCurrentWeatherEffect(moonInfo.Level);
            DawnWeatherEffectInfo weatherInfo = null;
            if (weatherEffect != null)
            {
                var method = typeof(WeatherEffectExtensions).GetMethod("GetDawnInfo", BindingFlags.Static | BindingFlags.NonPublic);
                if (method != null)
                {
                    weatherInfo = method.Invoke(null, new object[] { weatherEffect }) as DawnWeatherEffectInfo; 
                }
            }
            var ctx = new SpawnWeightContext(moonInfo,null,weatherInfo).WithExtra(SpawnWeightExtraKeys.RoutingPriceKey,moonInfo.DawnPurchaseInfo.Cost.Provide());
            float totalWeight = 0f;
            float selectedWeight = 0f;
            foreach (var dungeonInfo in LethalContent.Dungeons.Values)
            {
                float weight = dungeonInfo.Weights.GetFor(moonInfo, ctx) ?? 0f;
                if (weight <= 0f) continue;
                totalWeight += weight;
                if (ReferenceEquals(dungeonInfo, selectedDungeon)) //Are they ACTUALLY the same
                {
                    selectedWeight = weight;
                }
            }
            if (totalWeight <= 0f || selectedWeight <= 0f)
            {
                return -1f;
            }
            return selectedWeight / totalWeight * 100f;
        }
    }
}