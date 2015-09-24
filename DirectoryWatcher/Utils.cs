using System;
using System.Configuration;
using System.Globalization;

namespace Teamnet.DirectoryWatcher
{
    public static class Utils
    {
        public static string GetSetting(string key)
        {
            string value = ConfigurationManager.AppSettings[key];
            if(string.IsNullOrEmpty(value))
            {
                throw new ConfigurationErrorsException("Missing key in AppSettings: " + key);
            }
            return value;
        }

        public static T GetSetting<T>(string key, T defaultValue)
        {
            if(ConfigurationManager.AppSettings[key] == null)
            {
                return defaultValue;
            }
            return GetSetting<T>(key);
        }

        public static T GetSetting<T>(string key)
        {
            return Parse<T>(GetSetting(key));
        }

        private static T Parse<T>(string value)
        {
            if(typeof(T).IsEnum)
            {
                return (T)Enum.Parse(typeof(T), value, false);
            }
            var convertible = (IConvertible)value;
            return (T)convertible.ToType(typeof(T), CultureInfo.InvariantCulture);
        }
    }
}