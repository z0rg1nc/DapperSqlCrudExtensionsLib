using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BtmI2p.MiscUtils;
using NLog;
using Xunit;

namespace BtmI2p.Dapper.SqlCrudExtensions.Base
{
    public interface IBaseSqlPOCO
    {
    }
    [AttributeUsage(AttributeTargets.Property)]
    public class AutoIncrementHint : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class TableHintAttribute : Attribute
    {
        public string TableName { get; set; }

        public TableHintAttribute(string tableName)
        {
            TableName = tableName;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ColumnHintAttribute : Attribute
    {
        public string ColumnName { get; set; }

        public ColumnHintAttribute(string columnName)
        {
            ColumnName = columnName;
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
    public class UpdatableHintAttribute : Attribute
    {
    }
    /**/
    public interface ITypeToDbInfo<T1>
        where T1 : IBaseSqlPOCO
    {
        string TableName { get; }
        bool Updatable { get; }
        bool CheckAutoIncrementDefaultValues(T1 obj);
        string SelectString { get; }
        string FirstPropName { get; }
        string WhereString(params Expression<Func<T1, object>>[] keyPropNamesExpressions);
        string WhereString(List<string> wherePropNames);
        string ColumnNamesWithoutTable { get; }
        string PropNames { get; }
        string UpdateSetString(params Expression<Func<T1, object>>[] expressions);
        string UpdateSetString(List<string> updatePropNames);
        string UpdateSetStringAllExcept(List<string> exceptionPropNames);
        string ColumnNameWithTable<TProp>(Expression<Func<T1, TProp>> expression);
        string this[Expression<Func<T1, object>> expression] { get; }
        string ColumnNameWithoutTable<TProp>(Expression<Func<T1, TProp>> expression);
    }
    /**/
    public class TypeToDbBaseInfo<T1> : ITypeToDbInfo<T1>
        where T1 : IBaseSqlPOCO
    {
        public TypeToDbBaseInfo(char paramPrefix = '@')
        {
            _paramPrefix = paramPrefix;
            var t = typeof(T1);
            var tableAttribute = t.GetCustomAttribute<TableHintAttribute>();
            TableName = tableAttribute == null ? t.Name : tableAttribute.TableName;
            Updatable = t.GetCustomAttribute<UpdatableHintAttribute>() != null;
            if (t.GetFields().Any())
            {
                throw new ArgumentException("No fields allowed");
            }
            foreach (PropertyInfo propertyInfo in t.GetProperties())
            {
                var columnNameAttr = propertyInfo.GetCustomAttribute<ColumnHintAttribute>();
                bool autoIncrement = propertyInfo
                    .GetCustomAttribute<AutoIncrementHint>() != null;
                if (autoIncrement && !propertyInfo.PropertyType.IsValueType)
                    throw new ArgumentException(
                        "Autoincrement with not value type"
                    );
                var updatableAttr = propertyInfo
                    .GetCustomAttribute<UpdatableHintAttribute>() != null;
                if (autoIncrement && updatableAttr)
                    throw new ArgumentException(
                        "AutoIncrement with updatable");
                _propInfos.Add(
                    new PropDbInfo()
                    {
                        Name = propertyInfo.Name,
                        ColumnName = columnNameAttr != null
                            ? columnNameAttr.ColumnName
                            : propertyInfo.Name,
                        Updatable = updatableAttr,
                        AutoIncrement = autoIncrement,
                        PropType = propertyInfo.PropertyType,
                        PropertyInfoInstance = propertyInfo
                    }
                );
            }
        }

        private readonly char _paramPrefix;
        public string TableName { get; }
        public bool Updatable { get; }
        /**/
        private class PropDbInfo
        {
            public string ColumnName;
            public string Name;
            //public bool DontUpdate;
            public bool Updatable;
            public bool AutoIncrement;
            public Type PropType;
            public PropertyInfo PropertyInfoInstance;
        }

        public bool CheckAutoIncrementDefaultValues(T1 obj)
        {
            Assert.NotNull(obj);
            if (
                _propInfos
                    .Where(x => x.AutoIncrement)
                    .Any(
                        propInfo => !Activator.CreateInstance(propInfo.PropType)
                            .Equals(propInfo.PropertyInfoInstance.GetValue(obj))
                    )
            )
            {
                return false;
            }
            return true;
        }

        private readonly List<PropDbInfo> _propInfos = new List<PropDbInfo>();

        private string _cachedSelectString;
        public string SelectString
        {
            get
            {
                return _cachedSelectString ?? (_cachedSelectString = _propInfos.Select(
                    x => $"{TableName}.{x.ColumnName} as {x.Name}"
                ).Aggregate((a, b) => a + ", " + b));
            }
        }
        /**/
        public string FirstPropName => _propInfos.First().Name;
        /**/
        public string WhereString(
            params Expression<Func<T1, object>>[] keyPropNamesExpressions)
        {
            return WhereString(
                keyPropNamesExpressions.Select(MyNameof<T1>.Property).ToList()
            );
        }

        public string WhereString(List<string> wherePropNames)
        {
            Assert.NotNull(wherePropNames);
            Assert.True(wherePropNames.Any());
            Assert.Equal(wherePropNames, wherePropNames.Distinct());
            return _propInfos.Where(x => wherePropNames.Contains(x.Name)).Select(
                x => string.Format("{3}.{0}={1}{2}", x.ColumnName, _paramPrefix, x.Name, TableName)
            ).Aggregate((a, b) => $"{a} and {b}");
        }

        private string _cachedColumnNames;
        public string ColumnNamesWithoutTable
        {
            get
            {
                return _cachedColumnNames ?? (_cachedColumnNames = _propInfos.Select(
                    x => x.ColumnName)
                    .Aggregate((a, b) => $"{a},{b}"));
            }
        }

        private string _cachedPropNames;
        public string PropNames
        {
            get
            {
                return _cachedPropNames ?? (_cachedPropNames = _propInfos.Select(
                    x => _paramPrefix + x.Name)
                    .Aggregate((a, b) => $"{a},{b}"));
            }
        }

        public string UpdateSetString(params Expression<Func<T1, object>>[] expressions)
        {
            Assert.NotEmpty(expressions);
            var updatePropNames = expressions.Select(MyNameof<T1>.Property).ToList();
            return UpdateSetString(updatePropNames);
        }

        private readonly Logger _log = LogManager.GetCurrentClassLogger();
        public string UpdateSetString(List<string> updatePropNames)
        {
            Assert.NotNull(updatePropNames);
            Assert.NotEmpty(updatePropNames);
            var notUpdatableProps = _propInfos.Where(x => updatePropNames.Contains(x.Name) && !x.Updatable).ToList();
            if (notUpdatableProps.Any())
                _log.Error($"Not updatable props {notUpdatableProps.Select(_ => _.Name).ToList().WriteObjectToJson()} in {typeof(T1).FullName} class");
            return _propInfos.Where(x => updatePropNames.Contains(x.Name))
                .Select(x => string.Format("{3}.{0}={1}{2}", x.ColumnName, _paramPrefix, x.Name, TableName))
                    .Aggregate((a, b) => $"{a}, {b}");
        }

        public string UpdateSetStringAllExcept(List<string> exceptionPropNames)
        {
            Assert.NotNull(exceptionPropNames);
            Assert.True(exceptionPropNames.Count > 0);
            return _propInfos.Where(x => !exceptionPropNames.Contains(x.Name) && x.Updatable)
                .Select(x => string.Format("{3}.{0}={1}{2}", x.ColumnName, _paramPrefix, x.Name, TableName))
                    .Aggregate((a, b) => $"{a}, {b}");
        }
        public string ColumnNameWithTable<TProp>(Expression<Func<T1, TProp>> expression)
        {
            var propName = MyNameof<T1>.Property(expression);
            return $"{TableName}.{_propInfos.Single(x => x.Name == propName).ColumnName}";
        }
        public string this[Expression<Func<T1, object>> expression]
            => ColumnNameWithTable(expression);

        public string ColumnNameWithoutTable<TProp>(Expression<Func<T1, TProp>> expression)
        {
            var propName = MyNameof<T1>.Property(expression);
            return $"{_propInfos.Single(x => x.Name == propName).ColumnName}";
        }
    }

}
