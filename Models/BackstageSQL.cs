using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace breakfastshop.Models
{
    public class BackstageSQL
    {
        // 依你的要求：連線字串直接放在 Models
        private readonly string conn = "Server=localhost;Database=BreakfastShop;" +
            "User Id=Cadd0921@;Password=pL8!rV2qZ#91tGx4Nw@5;" +
            "Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;";

        private static readonly HashSet<string> TableWhitelist =
            new HashSet<string>(new[] { "Shop", "Meals", "Combo", "Table", "MealCategory" }, StringComparer.OrdinalIgnoreCase);

        // ---- 統一入口：Create / Update / Delete ----
        public int DoSQL(string action, string table, string id = null, Dictionary<string, object> data = null)
        {
            switch (action)
            {
                case "Create":
                    if (data == null || data.Count == 0) throw new ArgumentException("Create 需要 data");
                    return Insert(table, data);

                case "Update":
                    if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Update 需要 id");
                    if (data == null || data.Count == 0) throw new ArgumentException("Update 需要 data");
                    return Update(table, "Id", id, data);

                case "Delete":
                    if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Delete 需要 id");
                    return Delete(table, "Id", id);

                default:
                    throw new NotSupportedException("未知動作");
            }
        }

        // ----------------- 共用 CRUD -----------------
        private int Insert(string table, IDictionary<string, object> values)
        {
            string tbl = ValidateTable(table);
            var cols = values.Keys.Select(Bracket);
            var pars = values.Keys.Select(k => "@" + k);
            string sql = $"INSERT INTO {tbl} ({string.Join(",", cols)}) VALUES ({string.Join(",", pars)});";
            return ExecNonQuery(sql, values);
        }

        private int Update(string table, string idColumn, string idValue, IDictionary<string, object> values)
        {
            string tbl = ValidateTable(table);
            var sets = values.Keys.Select(k => $"{Bracket(k)}=@{k}");
            string sql = $"UPDATE {tbl} SET {string.Join(",", sets)} WHERE {Bracket(idColumn)}=@__id;";
            var param = new Dictionary<string, object>(values) { ["__id"] = ParseId(idValue) };
            return ExecNonQuery(sql, param);
        }

        private int Delete(string table, string idColumn, string idValue)
        {
            string tbl = ValidateTable(table);
            string sql = $"DELETE FROM {tbl} WHERE {Bracket(idColumn)}=@__id;";
            var param = new Dictionary<string, object> { ["__id"] = ParseId(idValue) };
            return ExecNonQuery(sql, param);
        }

        private int ExecNonQuery(string sql, IDictionary<string, object> param)
        {
            using (var c = new SqlConnection(conn))
            using (var cmd = new SqlCommand(sql, c) { CommandType = CommandType.Text })
            {
                foreach (var kv in param)
                {
                    if (kv.Value is decimal dec)
                    {
                        var p = cmd.Parameters.Add("@" + kv.Key, SqlDbType.Decimal);
                        p.Precision = 10; p.Scale = 2; p.Value = dec;
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);
                    }
                }
                c.Open();
                return cmd.ExecuteNonQuery();
            }
        }

        private static string ValidateTable(string table)
        {
            if (string.IsNullOrWhiteSpace(table)) throw new ArgumentException("table 必填");
            if (!TableWhitelist.Contains(table)) throw new InvalidOperationException($"表 {table} 不在白名單");
            return Bracket(table);
        }

        private static string Bracket(string identifier)
        {
            if (identifier.Contains("]")) throw new ArgumentException("識別字含非法字元 ']'");
            return $"[{identifier}]";
        }

        private static object ParseId(string id)
        {
            Guid g;
            return Guid.TryParse(id, out g) ? (object)g : id;
        }

        // ----------------- 查詢 (DataTable) -----------------
        public DataTable Query(string sql, IDictionary<string, object> param = null)
        {
            var dt = new DataTable();
            using (var c = new SqlConnection(conn))
            using (var cmd = new SqlCommand(sql, c) { CommandType = CommandType.Text })
            {
                if (param != null)
                {
                    foreach (var kv in param)
                    {
                        string name = "@" + kv.Key;
                        object val = kv.Value ?? DBNull.Value;

                        if (val is decimal dec)
                        {
                            var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
                            p.Precision = 10; p.Scale = 2; p.Value = dec;
                        }
                        else if (val == DBNull.Value)
                        {
                            // NULL 時補型別，避免型別衝突
                            if (kv.Key.Equals("ShopId", StringComparison.OrdinalIgnoreCase))
                                cmd.Parameters.Add(name, SqlDbType.UniqueIdentifier).Value = val;
                            else if (kv.Key.Equals("OnlyAct", StringComparison.OrdinalIgnoreCase) ||
                                     kv.Key.Equals("IsActive", StringComparison.OrdinalIgnoreCase))
                                cmd.Parameters.Add(name, SqlDbType.Bit).Value = val;
                            else
                                cmd.Parameters.Add(name, SqlDbType.NVarChar).Value = val;
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue(name, val);
                        }
                    }
                }

                c.Open();
                using (var rdr = cmd.ExecuteReader())
                {
                    dt.Load(rdr);
                }
            }
            return dt;
        }

        // DataTable -> List 字典：給 Controller 輕鬆輸出 JSON
        public static List<Dictionary<string, object>> ToList(DataTable dt)
        {
            var list = new List<Dictionary<string, object>>(dt.Rows.Count);
            foreach (DataRow row in dt.Rows)
            {
                var dict = new Dictionary<string, object>(dt.Columns.Count, StringComparer.OrdinalIgnoreCase);
                foreach (DataColumn col in dt.Columns)
                    dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                list.Add(dict);
            }
            return list;
        }
    }
}
