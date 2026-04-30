// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Cathei.BakingSheet.Internal
{
    public static class Config
    {
        public const string Comment = "$";
        public const string IndexDelimiter = ":";
        public const string SheetNameDelimiter = ".";

        // TODO: in .net standard 2.1 this is not needed
        public static readonly string[] IndexDelimiterArray = { IndexDelimiter };

        /// <summary>
        /// Split SheetName.SubName format.
        /// </summary>
        public static (string name, string subName) ParseSheetName(string name)
        {
            int idx = name.IndexOf(SheetNameDelimiter, StringComparison.Ordinal);

            if (idx == -1)
                return (name, null);

            return (name.Substring(0, idx), name.Substring(idx + 1));
        }

        /// <summary>
        /// Iterate properties with both getter and setter.
        /// </summary>
        public static IEnumerable<PropertyInfo> GetEligibleProperties(Type type)
        {
            const BindingFlags bindingFlags = BindingFlags.Public |
                                              BindingFlags.NonPublic |
                                              BindingFlags.Instance |
                                              BindingFlags.DeclaredOnly;

            while (type != null)
            {
                var properties = type.GetProperties(bindingFlags);

                foreach (var property in properties)
                {
                    if (property.IsDefined(typeof(NonSerializedAttribute)))
                        continue;

                    // Skip indexers ("Item" properties with index parameters); they need an index
                    // arg to read/write and can't be addressed as a single column.
                    if (property.GetIndexParameters().Length > 0)
                        continue;

                    if (property.GetMethod != null && property.SetMethod != null)
                        yield return property;
                }

                type = type.BaseType;
            }
        }

        /// <summary>
        /// Iterate fields that are eligible to be sheet columns. Mirrors Unity's serialization
        /// rule: public instance fields, OR private/protected/internal fields decorated with an
        /// attribute named "SerializeField" (any namespace — matched by attribute type name to
        /// avoid taking a hard UnityEngine dependency in the .NET build).
        ///
        /// Skipped: static / const / literal / read-only fields, compiler-generated backing fields
        /// for auto-properties, and anything marked with <see cref="NonSerializedAttribute"/>.
        /// </summary>
        public static IEnumerable<FieldInfo> GetEligibleSerializedFields(Type type)
        {
            const BindingFlags bindingFlags = BindingFlags.Public |
                                              BindingFlags.NonPublic |
                                              BindingFlags.Instance |
                                              BindingFlags.DeclaredOnly;

            while (type != null)
            {
                var fields = type.GetFields(bindingFlags);

                foreach (var field in fields)
                {
                    if (field.IsStatic || field.IsLiteral || field.IsInitOnly)
                        continue;

                    // Skip auto-property backing fields (named "<PropName>k__BackingField").
                    if (field.Name.Length > 0 && field.Name[0] == '<')
                        continue;

                    if (field.IsDefined(typeof(NonSerializedAttribute)))
                        continue;

                    if (!field.IsPublic && !HasSerializeFieldAttribute(field))
                        continue;

                    yield return field;
                }

                type = type.BaseType;
            }
        }

        private static bool HasSerializeFieldAttribute(FieldInfo field)
        {
            foreach (var attr in field.GetCustomAttributes(inherit: false))
            {
                if (attr.GetType().Name == "SerializeField")
                    return true;
            }
            return false;
        }

        public static PropertyInfo GetRowArrayProperty(Type type)
        {
            while (type != null)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SheetRowArray<,>))
                {
                    return type.GetProperty(nameof(ISheetRowArray.Arr));
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}
