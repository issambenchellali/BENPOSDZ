using System.Globalization;
using System.Collections.Generic;

namespace BENPOSDZ.Services
{
    public static class DataHelper
    {
        // للـ MySQL / Dapper: الأرقام تبقى أرقام، البوليوان تتحول لـ 0/1
        public static Dictionary<string, object?> SanitizeForSql(IDictionary<string, object?> dict)
        {
            var result = new Dictionary<string, object?>();
            foreach (var kvp in dict)
            {
                if (kvp.Value == null || kvp.Value == System.DBNull.Value) 
                { 
                    result[kvp.Key] = null; 
                    continue; 
                }
                
                result[kvp.Key] = kvp.Value switch
                {
                    bool b => b ? 1 : 0,
                    DateTime dt => dt,
                    _ => kvp.Value
                };
            }
            return result;
        }

        // للـ Supabase / REST API: الأرقام تُرسل كنصوص (لتجنب الفاصلة)، والبوليوان لـ true/false
        public static Dictionary<string, object?> SanitizeForApi(IDictionary<string, object?> dict)
        {
            var result = new Dictionary<string, object?>();
            foreach (var kvp in dict)
            {
                if (kvp.Value == null || kvp.Value == System.DBNull.Value) 
                { 
                    result[kvp.Key] = null; 
                    continue; 
                }
                
                result[kvp.Key] = kvp.Value switch
                {
                    bool b => b ? 1 : 0, 
                    decimal d => d.ToString(CultureInfo.InvariantCulture),
                    double d => d.ToString(CultureInfo.InvariantCulture),
                    float f => f.ToString(CultureInfo.InvariantCulture),
                    DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    _ => kvp.Value
                };
            }
            return result;
        }
    }
}