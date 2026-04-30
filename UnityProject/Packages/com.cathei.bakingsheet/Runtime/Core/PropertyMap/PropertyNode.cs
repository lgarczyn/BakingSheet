// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace Cathei.BakingSheet.Internal
{
    public abstract class PropertyNode
    {
        public delegate bool GetterDelegate(PropertyNode child, object obj, object key, out object value);
        public delegate void SetterDelegate(PropertyNode child, object obj, object key, object value);
        public delegate object ModifyDelegate(object original);

        public PropertyNode Parent { get; }
        public string FullPath { get; }

        public abstract Type IndexType { get; }
        public Type ValueType { get; }

        public GetterDelegate Getter { get; }
        public SetterDelegate Setter { get; }
        public PropertyInfo PropertyInfo { get; }

        // Field-backed nodes carry a FieldInfo here while PropertyInfo stays null. Consumers
        // that need attribute lookup (verifiers, contract resolvers) should treat both as
        // sources via Member when present.
        public FieldInfo FieldInfo { get; }
        public MemberInfo Member => (MemberInfo)PropertyInfo ?? FieldInfo;

        protected virtual bool IsLeaf => false;
        public virtual bool IsVertical => false;
        public abstract PropertyNode GetChild(string subpath);
        public virtual bool HasSubpath(string subpath) => false;

        // can be used when parent and child shares same column name
        public virtual PropertyNode ColumnNode => this;
        public ISheetValueConverter ValueConverter { get; protected set; }

        public abstract void UpdateIndex(object obj);
        public abstract int CalculateDepth();
        public abstract IEnumerable<PropertyNode> TraverseChildren(List<object> indexes);

        protected PropertyNode(PropertyNode parent, string fullPath, Type valueType,
            GetterDelegate getter, SetterDelegate setter, PropertyInfo propertyInfo,
            FieldInfo fieldInfo = null)
        {
            Parent = parent;
            FullPath = fullPath;
            ValueType = valueType;

            Getter = getter;
            Setter = setter;
            PropertyInfo = propertyInfo;
            FieldInfo = fieldInfo;
        }

        protected virtual object GetChildIndex(int vindex, IEnumerator<object> indexer)
        {
            if (IndexType == null)
                return null;

            indexer.MoveNext();
            return indexer.Current;
        }

        protected string AppendIndex(int depth)
        {
            return $"{FullPath}{Config.IndexDelimiter}{{{depth}}}";
        }

        public virtual int GetVerticalCount(ISheetRow row, IEnumerator<object> indexer)
        {
            return Parent?.GetVerticalCount(row, indexer) ?? 1;
        }

        public bool TryGetValue(ISheetRow row, int vindex, IEnumerator<object> indexer, out object value)
        {
            object obj = row;

            if (Parent != null)
                obj = Parent.GetValue(row, vindex, indexer);

            if (obj == null)
            {
                value = null;
                return false;
            }

            object index = Parent?.GetChildIndex(vindex, indexer);
            return Getter(this, obj, index, out value);
        }

        public object GetValue(ISheetRow row, int vindex, IEnumerator<object> indexer)
        {
            TryGetValue(row, vindex, indexer, out var value);
            return value;
        }

        public void SetValue(ISheetRow row, int vindex, IEnumerator<object> indexer, object value)
        {
            ModifyValue(row, vindex, indexer, _ => value);
        }

        public void ModifyValue(ISheetRow row, int vindex, IEnumerator<object> indexer, ModifyDelegate modifier)
        {
            if (Parent == null)
            {
                Getter(this, row, null, out var obj);
                modifier(obj);

                // there would be no setter for root node
                return;
            }

            Parent.ModifyValue(row, vindex, indexer, parentObj =>
            {
                if (parentObj == null)
                    return null;

                object index = Parent?.GetChildIndex(vindex, indexer);
                Getter(this, parentObj, index, out var obj);

                // for leaf nodes there might be no default constructor available
                if (obj == null && !IsLeaf)
                    obj = Activator.CreateInstance(ValueType);

                obj = modifier(obj);

                Debug.Assert(Setter != null);

                Setter(this, parentObj, index, obj);

                return parentObj;
            });
        }

        public static PropertyNode Create(
            PropertyNode parent, string fullPath, Type type,
            GetterDelegate getter, SetterDelegate setter, PropertyInfo propertyInfo,
            ISheetContractResolver resolver, int depth, FieldInfo fieldInfo = null)
        {
            if (typeof(IVerticalList).IsAssignableFrom(type))
            {
                return new PropertyNodeList(parent, fullPath, type,
                    getter, setter, propertyInfo, resolver, depth, true, fieldInfo);
            }

            if (typeof(IList).IsAssignableFrom(type))
            {
                return new PropertyNodeList(parent, fullPath, type,
                    getter, setter, propertyInfo, resolver, depth, false, fieldInfo);
            }

            if (typeof(IDictionary).IsAssignableFrom(type))
            {
                return new PropertyNodeDictionary(parent, fullPath, type,
                    getter, setter, propertyInfo, resolver, depth, fieldInfo);
            }

            return new PropertyNodeObject(parent, fullPath, type,
                    getter, setter, propertyInfo, resolver, depth, fieldInfo);
        }
    }
}
