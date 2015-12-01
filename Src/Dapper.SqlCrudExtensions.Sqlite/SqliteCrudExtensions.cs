using System;
using System.Collections.Concurrent;
using System.Data;
using System.Threading.Tasks;
using BtmI2p.Community.CsharpSqlite.SQLiteClient;
using BtmI2p.Dapper.SqlCrudExtensions.Base;
using Dapper;
using Xunit;

namespace BtmI2p.Dapper.SqlCrudExtensions.Sqlite
{
    public interface ISqlitePOCO : IBaseSqlPOCO
    {
    }

    public static class SqliteCrudExtensions
    {
        private static readonly ConcurrentDictionary<Type, object> TypeMappingInfo
               = new ConcurrentDictionary<Type, object>();

        public static ITypeToDbInfo<T1> GetDbInfo<T1>()
            where T1 : class, ISqlitePOCO, new()
        {
            return GetDbInfo((T1)null);
        }
        public static ITypeToDbInfo<T1> GetDbInfo<T1>(T1 obj)
            where T1 : class, ISqlitePOCO, new()
        {
            return (ITypeToDbInfo<T1>)TypeMappingInfo.GetOrAdd(
                typeof(T1),
                x => new TypeToDbBaseInfo<T1>(':')
            );
        }
        /**/
        public static async Task<int> InsertAsync<T1>(
            this SqliteConnection conn,
            T1 obj,
            IDbTransaction trans = null,
            bool ignore = false
        )
            where T1 : class, ISqlitePOCO, new()
        {
            Assert.NotNull(obj);
            var typeDbInfo = GetDbInfo<T1>();
            if (!typeDbInfo.CheckAutoIncrementDefaultValues(obj))
                throw new ArgumentException(
                    "AutoIncrement properties should be equal to default(T)"
                );
            var ignoreString = ignore ? " or ignore" : "";
            var sqlString =
                $"insert{ignoreString} into {typeDbInfo.TableName} ({typeDbInfo.ColumnNamesWithoutTable}) values ({typeDbInfo.PropNames});";
            return await conn.ExecuteAsync(
                sqlString,
                obj,
                trans
            ).ConfigureAwait(false);
        }
    }
}
