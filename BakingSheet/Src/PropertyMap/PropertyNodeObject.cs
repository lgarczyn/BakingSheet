// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Cathei.BakingSheet.Internal
{
    public class PropertyNodeObject : PropertyNode
    {
        private Dictionary<string, PropertyNode> _children;

        public override Type IndexType => null;

        protected override bool IsLeaf => _children == null;

        public PropertyNodeObject(PropertyNode parent, string fullPath, Type valueType,
            GetterDelegate getter, SetterDelegate setter, PropertyInfo propertyInfo,
            ISheetContractResolver resolver, int depth, FieldInfo fieldInfo = null)
            : base(parent, fullPath, valueType, getter, setter, propertyInfo, fieldInfo)
        {
            GenerateChildren(resolver, depth);
        }

        public override PropertyNode GetChild(string subpath) => _children?[subpath];

        public bool RemoveChild(string subpath) => _children?.Remove(subpath) ?? false;

        public override bool HasSubpath(string subpath) => _children?.ContainsKey(subpath) ?? false;

        private string AppendPath(string subpath)
        {
            if (FullPath == null)
                return subpath;

            return $"{FullPath}{Config.IndexDelimiter}{subpath}";
        }

        public override void UpdateIndex(object obj)
        {
            if (IsLeaf)
                return;

            foreach (var child in _children.Values)
            {
                child.Getter(child, obj, null, out var elem);
                if (elem != null)
                    child.UpdateIndex(elem);
            }
        }

        public override int CalculateDepth()
        {
            if (IsLeaf)
                return 0;

            int depth = 0;

            foreach (var child in _children.Values)
                depth = Math.Max(depth, child.CalculateDepth());

            return depth;
        }

        public override IEnumerable<PropertyNode> TraverseChildren(List<object> indexes)
        {
            if (IsLeaf)
            {
                yield return this;
                yield break;
            }

            // Id column should come first
            if (_children.TryGetValue(nameof(ISheetRow.Id), out var idChild))
            {
                foreach (var node in idChild.TraverseChildren(indexes))
                    yield return node;
            }

            foreach (var child in _children.Values)
            {
                if (child == idChild)
                    continue;

                foreach (var node in child.TraverseChildren(indexes))
                    yield return node;
            }
        }

        internal static bool ValueGetter(PropertyNode child, object obj, object key, out object value)
        {
            Debug.Assert(child.PropertyInfo != null);
            value = child.PropertyInfo.GetValue(obj);
            return true;
        }

        private static void ValueSetter(PropertyNode child, object obj, object key, object value)
        {
            Debug.Assert(child.PropertyInfo != null);
            child.PropertyInfo.SetValue(obj, value);
        }

        internal static bool FieldValueGetter(PropertyNode child, object obj, object key, out object value)
        {
            Debug.Assert(child.FieldInfo != null);
            value = child.FieldInfo.GetValue(obj);
            return true;
        }

        private static void FieldValueSetter(PropertyNode child, object obj, object key, object value)
        {
            Debug.Assert(child.FieldInfo != null);
            child.FieldInfo.SetValue(obj, value);
        }

        private void GenerateChildren(ISheetContractResolver resolver, int depth)
        {
            ValueConverter = resolver.GetValueConverter(PropertyInfo) ??
                             resolver.GetValueConverter(ValueType);

            // leaf node (convertable)
            if (ValueConverter != null)
                return;

            _children = new Dictionary<string, PropertyNode>();

            bool isRoot = Parent == null;

            foreach (PropertyInfo propertyInfo in Config.GetEligibleProperties(ValueType))
            {
                // "Arr" is reserved for SheetRowArray
                if (isRoot && propertyInfo.Name == nameof(ISheetRowArray.Arr))
                    continue;

                var childPath = AppendPath(propertyInfo.Name);
                var child = PropertyNode.Create(this, childPath, propertyInfo.PropertyType,
                    ValueGetter, ValueSetter, propertyInfo, resolver, depth);

                _children.Add(propertyInfo.Name, child);
            }

            // Also reflect Unity-style serialized fields (public fields and [SerializeField] privates).
            // Done after properties so a property with the same name wins the slot if both exist.
            foreach (FieldInfo fieldInfo in Config.GetEligibleSerializedFields(ValueType))
            {
                if (_children.ContainsKey(fieldInfo.Name))
                    continue;

                var childPath = AppendPath(fieldInfo.Name);
                var child = PropertyNode.Create(this, childPath, fieldInfo.FieldType,
                    FieldValueGetter, FieldValueSetter, propertyInfo: null, resolver, depth, fieldInfo);

                _children.Add(fieldInfo.Name, child);
            }
        }
    }
}
