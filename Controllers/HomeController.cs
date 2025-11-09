using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Mvc;
using breakfastshop.Models;

namespace breakfastshop.Controllers
{
    public class HomeController : Controller
    {
        private readonly BackstageSQL _db = new BackstageSQL();

        private const string AdminAccountName = "admin";
        private const string AdminDisplayName = "系統管理員";
        // 預設管理員密碼：Breakfast@2024（以 SHA-256 儲存）
        private const string AdminPasswordHash = "c881fa2020986c9ce4315e8fb5b91c54c4627ad01115b2a56b11d4dba17a5507";
        private static readonly Guid AdminId = Guid.Empty;
        private static readonly string[] AllowedOrderStatuses = { "Pending", "Preparing", "Completed", "Cancelled" };
        private static readonly char[] TakeoutCodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

        private static bool IsAdminAccount(string account)
        {
            return string.Equals(account, AdminAccountName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool VerifyAdminPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return false;
            string hash = HashPassword(password);
            return string.Equals(hash, AdminPasswordHash, StringComparison.OrdinalIgnoreCase);
        }
        
        private static string HashPassword(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    sb.AppendFormat("{0:x2}", b);
                }
                return sb.ToString();
            }
        }

        private static string NormalizeOrderType(string orderType)
        {
            if (string.IsNullOrWhiteSpace(orderType)) return null;
            if (string.Equals(orderType, "DineIn", StringComparison.OrdinalIgnoreCase)) return "DineIn";
            if (string.Equals(orderType, "TakeOut", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(orderType, "Takeaway", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(orderType, "ToGo", StringComparison.OrdinalIgnoreCase))
                return "TakeOut";
            if (string.Equals(orderType, "內用", StringComparison.OrdinalIgnoreCase)) return "DineIn";
            if (string.Equals(orderType, "外帶", StringComparison.OrdinalIgnoreCase)) return "TakeOut";
            return null;
        }

        private static string NormalizeOrderStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                throw new ArgumentException("狀態不可為空");

            foreach (var allowed in AllowedOrderStatuses)
            {
                if (string.Equals(status, allowed, StringComparison.OrdinalIgnoreCase))
                    return allowed;
            }

            throw new ArgumentException("不支援的訂單狀態");
        }

        private static string NormalizeOrderStatusForFilter(string status)
        {
            if (string.IsNullOrWhiteSpace(status) ||
                string.Equals(status, "All", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "全部", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return NormalizeOrderStatus(status);
        }

        private static string GenerateTakeoutCode(RandomNumberGenerator rng)
        {
            var buffer = new char[8];
            var randomBytes = new byte[8];
            int index = 0;
            while (index < buffer.Length)
            {
                rng.GetBytes(randomBytes);
                for (int i = 0; i < randomBytes.Length && index < buffer.Length; i++)
                {
                    buffer[index++] = TakeoutCodeCharacters[randomBytes[i] % TakeoutCodeCharacters.Length];
                }
            }

            return new string(buffer);
        }

        private string GenerateUniqueTakeoutCode()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    string code = GenerateTakeoutCode(rng);
                    var dt = _db.Query("SELECT TOP 1 1 FROM dbo.Orders WHERE TakeoutCode=@Code", new Dictionary<string, object>
                    {
                        ["Code"] = code
                    });

                    if (dt.Rows.Count == 0)
                        return code;
                }
            }

            throw new InvalidOperationException("無法產生外帶取餐碼，請稍後再試");
        }

        private TableInfo EnsureTableExists(Guid tableId, Guid? expectedShopId)
        {
            var dt = _db.Query("SELECT TOP 1 Number, ShopId FROM dbo.[Table] WHERE Id=@Id AND IsActive=1", new Dictionary<string, object>
            {
                ["Id"] = tableId
            });

            if (dt.Rows.Count == 0)
                throw new ArgumentException("找不到指定的桌號");

            var row = dt.Rows[0];
            var info = new TableInfo
            {
                Id = tableId,
                Number = Convert.ToInt32(row["Number"]),
                ShopId = (Guid)row["ShopId"]
            };

            if (expectedShopId.HasValue && expectedShopId.Value != info.ShopId)
                throw new ArgumentException("桌號不屬於指定的店家");

            return info;
        }

        private Dictionary<Guid, MealSnapshot> LoadMealsSnapshot(IEnumerable<Guid> mealIds)
        {
            var ids = mealIds?.Distinct().ToList() ?? new List<Guid>();
            if (ids.Count == 0) return new Dictionary<Guid, MealSnapshot>();

            var param = new Dictionary<string, object>();
            var placeholders = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                string key = "M" + i;
                param[key] = ids[i];
                placeholders.Add("@" + key);
            }

            string sql = $"SELECT Id, Name, Money FROM dbo.Meals WHERE Id IN ({string.Join(",", placeholders)})";
            var dt = _db.Query(sql, param);

            var lookup = new Dictionary<Guid, MealSnapshot>(dt.Rows.Count);
            foreach (DataRow row in dt.Rows)
            {
                var id = (Guid)row["Id"];
                lookup[id] = new MealSnapshot
                {
                    Id = id,
                    Name = row["Name"]?.ToString() ?? string.Empty,
                    Price = row["Money"] == DBNull.Value ? 0m : Convert.ToDecimal(row["Money"])
                };
            }

            return lookup;
        }

        public ActionResult Index() 
        { 
            return View(); 
        }
        public ActionResult Backstage() 
        { 
            return View(); 
        }
        //點餐介面(分外帶、內用)
        public ActionResult Order() 
        {
            return View();
        }
        //接收訂單介面
        public ActionResult Receiving()
        {
            return View();
        }
        public ActionResult Login()
        {
            return View();
        }

        #region Ordering
        [HttpPost]
        public JsonResult CreateOrder(CreateOrderRequest request)
        {
            try
            {
                if (request == null) throw new ArgumentException("缺少訂單資料");

                string orderType = NormalizeOrderType(request.OrderType);
                if (orderType == null)
                    throw new ArgumentException("訂單類型僅支援內用或外帶");

                var validItems = (request.Items ?? new List<OrderItemRequest>())
                    .Where(item => item != null && item.MealId != Guid.Empty && item.Quantity > 0)
                    .ToList();

                if (validItems.Count == 0)
                    throw new ArgumentException("請至少選擇一項餐點");

                Guid? tableId = null;
                int? tableNumber = null;
                TableInfo tableInfo = null;
                if (string.Equals(orderType, "DineIn", StringComparison.OrdinalIgnoreCase))
                {
                    if (!request.TableId.HasValue || request.TableId.Value == Guid.Empty)
                        throw new ArgumentException("內用訂單需選擇桌號");

                    tableInfo = EnsureTableExists(request.TableId.Value, request.ShopId);
                    tableNumber = tableInfo.Number;
                    tableId = tableInfo.Id;
                }

                var mealLookup = LoadMealsSnapshot(validItems.Select(i => i.MealId));
                foreach (var item in validItems)
                {
                    if (!mealLookup.ContainsKey(item.MealId))
                        throw new ArgumentException("部分餐點不存在或已下架");
                }

                string takeoutCode = orderType == "TakeOut" ? GenerateUniqueTakeoutCode() : null;
                var now = DateTime.UtcNow;
                Guid orderId = Guid.NewGuid();
                string notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
                Guid? shopId = request.ShopId.HasValue && request.ShopId.Value != Guid.Empty ? request.ShopId : null;
                if (tableInfo != null)
                {
                    shopId = shopId ?? tableInfo.ShopId;
                }

                using (var conn = _db.CreateConnection())
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        using (var cmd = new SqlCommand(@"INSERT INTO dbo.Orders (Id, ShopId, OrderType, TableId, TakeoutCode, Notes, Status, CreatedAt, UpdatedAt)
VALUES (@Id, @ShopId, @OrderType, @TableId, @TakeoutCode, @Notes, @Status, @CreatedAt, @UpdatedAt);", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@Id", orderId);
                            cmd.Parameters.AddWithValue("@ShopId", shopId.HasValue ? (object)shopId.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@OrderType", orderType);
                            cmd.Parameters.AddWithValue("@TableId", tableId.HasValue ? (object)tableId.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@TakeoutCode", string.IsNullOrEmpty(takeoutCode) ? (object)DBNull.Value : takeoutCode);
                            cmd.Parameters.AddWithValue("@Notes", string.IsNullOrEmpty(notes) ? (object)DBNull.Value : notes);
                            cmd.Parameters.AddWithValue("@Status", AllowedOrderStatuses[0]);
                            cmd.Parameters.AddWithValue("@CreatedAt", now);
                            cmd.Parameters.AddWithValue("@UpdatedAt", now);
                            cmd.ExecuteNonQuery();
                        }

                        foreach (var item in validItems)
                        {
                            var meal = mealLookup[item.MealId];
                            using (var cmd = new SqlCommand(@"INSERT INTO dbo.OrderItems (Id, OrderId, MealId, MealName, Quantity, UnitPrice, Notes, CreateDate)
VALUES (@Id, @OrderId, @MealId, @MealName, @Quantity, @UnitPrice, @Notes, @CreateDate);", conn, tx))
                            {
                                cmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
                                cmd.Parameters.AddWithValue("@OrderId", orderId);
                                cmd.Parameters.AddWithValue("@MealId", meal.Id);
                                cmd.Parameters.AddWithValue("@MealName", meal.Name);
                                cmd.Parameters.AddWithValue("@Quantity", item.Quantity);
                                var priceParam = cmd.Parameters.Add("@UnitPrice", SqlDbType.Decimal);
                                priceParam.Precision = 10;
                                priceParam.Scale = 2;
                                priceParam.Value = meal.Price;
                                cmd.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(item.Notes) ? (object)DBNull.Value : item.Notes.Trim());
                                cmd.Parameters.AddWithValue("@CreateDate", now);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        tx.Commit();
                    }
                }

                var items = new List<object>(validItems.Count);
                decimal total = 0m;
                foreach (var item in validItems)
                {
                    var meal = mealLookup[item.MealId];
                    decimal subtotal = meal.Price * item.Quantity;
                    total += subtotal;
                    items.Add(new
                    {
                        mealId = meal.Id,
                        mealName = meal.Name,
                        quantity = item.Quantity,
                        unitPrice = meal.Price,
                        notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim(),
                        subtotal
                    });
                }

                return Json(new
                {
                    ok = true,
                    order = new
                    {
                        id = orderId,
                        orderType,
                        tableId,
                        tableNumber,
                        shopId,
                        takeoutCode,
                        status = AllowedOrderStatuses[0],
                        createdAt = now,
                        notes,
                        total,
                        items
                    }
                });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, error = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetReceivingOrders(string status)
        {
            try
            {
                string filterStatus = NormalizeOrderStatusForFilter(status);
                var sql = @"SELECT o.Id, o.ShopId, s.Name AS ShopName, o.OrderType, o.TableId, t.Number AS TableNumber, t.Zone AS TableZone, o.TakeoutCode, o.Status, o.CreatedAt, o.Notes AS OrderNotes,
       i.Id AS ItemId, i.MealId, i.MealName, i.Quantity, i.UnitPrice, i.Notes AS ItemNotes
FROM dbo.Orders AS o
LEFT JOIN dbo.[Table] AS t ON o.TableId = t.Id
LEFT JOIN dbo.Shop AS s ON o.ShopId = s.Id
INNER JOIN dbo.OrderItems AS i ON o.Id = i.OrderId
WHERE (@Status IS NULL OR o.Status=@Status)
ORDER BY o.CreatedAt ASC, i.CreateDate ASC;";

                var param = new Dictionary<string, object>
                {
                    ["Status"] = filterStatus == null ? (object)DBNull.Value : filterStatus
                };

                var dt = _db.Query(sql, param);
                var orders = new Dictionary<Guid, ReceivingOrderDto>(dt.Rows.Count);

                foreach (DataRow row in dt.Rows)
                {
                    var orderId = (Guid)row["Id"];
                    if (!orders.TryGetValue(orderId, out var order))
                    {
                        order = new ReceivingOrderDto
                        {
                            Id = orderId,
                            ShopId = row["ShopId"] == DBNull.Value ? (Guid?)null : (Guid)row["ShopId"],
                            ShopName = row["ShopName"] == DBNull.Value ? null : row["ShopName"].ToString(),
                            OrderType = row["OrderType"]?.ToString(),
                            TableId = row["TableId"] == DBNull.Value ? (Guid?)null : (Guid)row["TableId"],
                            TableNumber = row["TableNumber"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["TableNumber"]),
                            TableZone = row["TableZone"] == DBNull.Value ? null : row["TableZone"].ToString(),
                            TakeoutCode = row["TakeoutCode"]?.ToString(),
                            Status = row["Status"]?.ToString(),
                            CreatedAt = (DateTime)row["CreatedAt"],
                            Notes = row["OrderNotes"]?.ToString(),
                            Items = new List<ReceivingOrderItemDto>()
                        };
                        orders[orderId] = order;
                    }

                    order.Items.Add(new ReceivingOrderItemDto
                    {
                        Id = (Guid)row["ItemId"],
                        MealId = (Guid)row["MealId"],
                        MealName = row["MealName"]?.ToString(),
                        Quantity = Convert.ToInt32(row["Quantity"]),
                        UnitPrice = row["UnitPrice"] == DBNull.Value ? 0m : Convert.ToDecimal(row["UnitPrice"]),
                        Notes = row["ItemNotes"]?.ToString()
                    });
                }

                return Json(orders.Values.ToList(), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, error = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult UpdateOrderStatus(UpdateOrderStatusRequest request)
        {
            try
            {
                if (request == null)
                    throw new ArgumentException("缺少訂單資料");

                if (request.OrderId == Guid.Empty)
                    throw new ArgumentException("缺少訂單 Id");

                string normalized = NormalizeOrderStatus(request.Status);
                var data = new Dictionary<string, object>
                {
                    ["Status"] = normalized,
                    ["UpdatedAt"] = DateTime.UtcNow
                };

                int rows = _db.DoSQL("Update", "Orders", id: request.OrderId.ToString(), data: data);
                return Json(new { ok = rows > 0, rows });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, error = ex.Message });
            }
        }
        #endregion

        private class MealSnapshot
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public decimal Price { get; set; }
        }

        private class TableInfo
        {
            public Guid Id { get; set; }
            public Guid ShopId { get; set; }
            public int Number { get; set; }
        }

        [HttpPost]
        public JsonResult Login(LoginRequest request)
        {
            try
            {
                if (request == null) throw new ArgumentException("缺少登入資料");

                string account = request.Account?.Trim();
                string password = request.Password?.Trim();

                if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
                    throw new ArgumentException("帳號與密碼必填");
                if (password.Length < 6)
                    throw new ArgumentException("密碼錯誤");

                if (IsAdminAccount(account))
                {
                    if (!VerifyAdminPassword(password))
                    {
                        Response.StatusCode = 401;
                        return Json(new { ok = false, error = "帳號或密碼錯誤" });
                    }

                    return Json(new
                    {
                        ok = true,
                        id = AdminId,
                        name = AdminDisplayName,
                        account = AdminAccountName,
                        isAdmin = true
                    });
                }

                var sql = @"SELECT TOP 1 Id, Name, Account
                            FROM dbo.Shop
                            WHERE Account=@Account AND Password=@Password
                              AND (IsActive IS NULL OR IsActive=1)";

                var dt = _db.Query(sql, new Dictionary<string, object>
                {
                    ["Account"] = account,
                    ["Password"] = password
                });

                if (dt.Rows.Count == 0)
                {
                    Response.StatusCode = 401;
                    return Json(new { ok = false, error = "帳號或密碼錯誤" });
                }

                var row = dt.Rows[0];
                return Json(new
                {
                    ok = true,
                    id = row["Id"],
                    name = row["Name"],
                    account = row["Account"],
                    isAdmin = false
                });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, error = ex.Message });
            }
        }

        #region Meals
        [HttpGet]
        public JsonResult GetMeals(Guid? shopId, bool? onlyActive)
        {
            var sql = @"SELECT m.Id, m.ShopId, s.Name AS ShopName, m.Name, m.Money, m.IsActive, m.Element, m.CategoryId, cat.Name AS CategoryName, m.CreateDate, m.UpdateDate
                        FROM dbo.Meals AS m
                        LEFT JOIN dbo.Shop AS s ON m.ShopId = s.Id
                        LEFT JOIN dbo.MealCategory AS cat ON m.CategoryId = cat.Id
                        WHERE (@ShopId IS NULL OR m.ShopId=@ShopId)
                          AND (@OnlyAct IS NULL OR m.IsActive=@OnlyAct)
                        ORDER BY ISNULL(cat.SortOrder, 0) ASC, m.Name ASC";
            var dt = _db.Query(sql, new Dictionary<string, object>
            {
                ["ShopId"] = (object)shopId ?? DBNull.Value,
                ["OnlyAct"] = (object)onlyActive ?? DBNull.Value
            });
            return Json(BackstageSQL.ToList(dt), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult UpdateMeals(string actionType, MealDto model)
        {
            try
            {
                if (string.Equals(actionType, "Delete", StringComparison.OrdinalIgnoreCase))
                {
                    if (model == null || model.Id == Guid.Empty) throw new ArgumentException("缺少 Id");
                    int affectedDelete = _db.DoSQL("Delete", "Meals", id: model.Id.ToString());
                    return Json(new { ok = affectedDelete > 0, rows = affectedDelete });
                }

                if (model == null) throw new ArgumentException("缺少資料");
                if (model.ShopId == Guid.Empty) throw new ArgumentException("缺少 ShopId");
                if (string.IsNullOrWhiteSpace(model.Name)) throw new ArgumentException("餐點名稱必填");
                if (model.Money.HasValue && model.Money.Value < 0) throw new ArgumentException("金額不可為負值");

                var data = new Dictionary<string, object>();
                if (string.Equals(actionType, "Create", StringComparison.OrdinalIgnoreCase))
                {
                    data["Id"] = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;
                    data["CreateDate"] = DateTime.UtcNow;
                }
                else
                {
                    if (model.Id == Guid.Empty) throw new ArgumentException("缺少 Id");
                }

                data["ShopId"] = model.ShopId;
                data["Name"] = model.Name;
                if (model.Money.HasValue) data["Money"] = model.Money.Value;
                if (model.IsActive.HasValue) data["IsActive"] = model.IsActive.Value;
                data["Element"] = model.Element ?? (object)DBNull.Value;
                if (model.CategoryId.HasValue && model.CategoryId.Value != Guid.Empty)
                    data["CategoryId"] = model.CategoryId.Value;
                else
                    data["CategoryId"] = DBNull.Value;
                data["UpdateDate"] = DateTime.UtcNow;

                int affected;
                if (string.Equals(actionType, "Create", StringComparison.OrdinalIgnoreCase))
                    affected = _db.DoSQL("Create", "Meals", data: data);
                else if (string.Equals(actionType, "Update", StringComparison.OrdinalIgnoreCase))
                    affected = _db.DoSQL("Update", "Meals", id: model.Id.ToString(), data: data);
                else
                    throw new NotSupportedException("actionType 僅支援 Create/Update/Delete");

                return Json(new { ok = affected > 0, rows = affected, id = data.ContainsKey("Id") ? data["Id"] : model.Id });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, error = ex.Message });
            }
        }
        #endregion

        #region MealCategory
        [HttpGet]
        public JsonResult GetMealCategories(Guid? shopId, bool? onlyActive)
        {
            var sql = @"SELECT c.Id, c.ShopId, s.Name AS ShopName, c.Name, c.SortOrder, c.IsActive, c.CreateDate, c.UpdateDate
                        FROM dbo.MealCategory AS c
                        LEFT JOIN dbo.Shop AS s ON c.ShopId = s.Id
                        WHERE (@ShopId IS NULL OR c.ShopId=@ShopId)
                          AND (@OnlyAct IS NULL OR c.IsActive=@OnlyAct)
                        ORDER BY c.SortOrder ASC, c.Name ASC";
            var dt = _db.Query(sql, new Dictionary<string, object>
            {
                ["ShopId"] = (object)shopId ?? DBNull.Value,
                ["OnlyAct"] = (object)onlyActive ?? DBNull.Value
            });
            return Json(BackstageSQL.ToList(dt), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult UpdateMealCategory(string actionType, MealCategoryDto model)
        {
            try
            {
                if (string.Equals(actionType, "Delete", StringComparison.OrdinalIgnoreCase))
                {
                    if (model == null || model.Id == Guid.Empty) throw new ArgumentException("缺少 Id");
                    int affectedDelete = _db.DoSQL("Delete", "MealCategory", id: model.Id.ToString());
                    return Json(new { ok = affectedDelete > 0, rows = affectedDelete });
                }

                if (model == null) throw new ArgumentException("缺少資料");
                if (model.ShopId == Guid.Empty) throw new ArgumentException("缺少 ShopId");
                if (string.IsNullOrWhiteSpace(model.Name)) throw new ArgumentException("分類名稱必填");

                var data = new Dictionary<string, object>();
                if (string.Equals(actionType, "Create", StringComparison.OrdinalIgnoreCase))
                {
                    data["Id"] = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;
                    data["CreateDate"] = DateTime.UtcNow;
                }
                else if (model.Id == Guid.Empty) throw new ArgumentException("缺少 Id");

                data["ShopId"] = model.ShopId;
                data["Name"] = model.Name;
                data["SortOrder"] = model.SortOrder ?? 0;
                if (model.IsActive.HasValue) data["IsActive"] = model.IsActive.Value;
                data["UpdateDate"] = DateTime.UtcNow;

                int affected = string.Equals(actionType, "Create", StringComparison.OrdinalIgnoreCase)
                    ? _db.DoSQL("Create", "MealCategory", data: data)
                    : _db.DoSQL("Update", "MealCategory", id: model.Id.ToString(), data: data);

                return Json(new { ok = affected > 0, rows = affected, id = data.ContainsKey("Id") ? data["Id"] : model.Id });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, error = ex.Message });
            }
        }
        #endregion

        #region Combo
        [HttpGet]
        public JsonResult GetCombo(Guid? shopId, bool? onlyActive)
        {
            var sql = @"SELECT c.Id, c.ShopId, s.Name AS ShopName, c.Title, c.ComboMeal, c.Money, c.IsActive, c.CreateDate, c.UpdateDate
                        FROM dbo.Combo AS c
                        LEFT JOIN dbo.Shop AS s ON c.ShopId = s.Id
                        WHERE (@ShopId IS NULL OR c.ShopId=@ShopId)
                          AND (@OnlyAct IS NULL OR c.IsActive=@OnlyAct)
                        ORDER BY c.CreateDate DESC";
            var dt = _db.Query(sql, new Dictionary<string, object>
            {
                ["ShopId"] = (object)shopId ?? DBNull.Value,
                ["OnlyAct"] = (object)onlyActive ?? DBNull.Value
            });
            return Json(BackstageSQL.ToList(dt), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult UpdateCombo(string actionType, ComboDto model)
        {
            try
            {
                if (string.Equals(actionType, "Delete", StringComparison.OrdinalIgnoreCase))
                {
                    if (model == null || model.Id == Guid.Empty) throw new ArgumentException("缺少 Id");
                    int affectedDelete = _db.DoSQL("Delete", "Combo", id: model.Id.ToString());
                    return Json(new { ok = affectedDelete > 0, rows = affectedDelete });
                }

                if (model == null) throw new ArgumentException("缺少資料");
                if (model.ShopId == Guid.Empty) throw new ArgumentException("缺少 ShopId");
                if (string.IsNullOrWhiteSpace(model.Name)) throw new ArgumentException("套餐名稱必填");
                if (string.IsNullOrWhiteSpace(model.ComboMeal)) throw new ArgumentException("套餐內容必填");
                if (model.Money.HasValue && model.Money.Value < 0) throw new ArgumentException("金額不可為負值");

                var data = new Dictionary<string, object>();
                if (string.Equals(actionType, "Create", StringComparison.OrdinalIgnoreCase))
                {
                    data["Id"] = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;
                    data["CreateDate"] = DateTime.UtcNow;
                }
                else if (model.Id == Guid.Empty) throw new ArgumentException("缺少 Id");

                data["ShopId"] = model.ShopId;
                data["Title"] = model.Name ?? (object)DBNull.Value;
                string comboPayload = string.IsNullOrWhiteSpace(model.ComboMeal) ? "[]" : model.ComboMeal;
                data["ComboMeal"] = comboPayload;
                if (model.Money.HasValue) data["Money"] = model.Money.Value;
                if (model.IsActive.HasValue) data["IsActive"] = model.IsActive.Value;
                data["UpdateDate"] = DateTime.UtcNow;

                int affected = string.Equals(actionType, "Create", StringComparison.OrdinalIgnoreCase)
                    ? _db.DoSQL("Create", "Combo", data: data)
                    : _db.DoSQL("Update", "Combo", id: model.Id.ToString(), data: data);

                return Json(new { ok = affected > 0, rows = affected, id = data.ContainsKey("Id") ? data["Id"] : model.Id });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, error = ex.Message });
            }
        }
        #endregion

        #region Shop
        [HttpGet]
        public JsonResult GetShop(bool? onlyActive)
        {
            var sql = @"SELECT Id, Name, Phone, Account, Password, Addr, IsActive, CreateDate, UpdateDate
                        FROM dbo.Shop
                        WHERE (@OnlyAct IS NULL OR IsActive=@OnlyAct)
                        ORDER BY Name ASC";
            var dt = _db.Query(sql, new Dictionary<string, object>
            {
                ["OnlyAct"] = (object)onlyActive ?? DBNull.Value
            });
            return Json(BackstageSQL.ToList(dt), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult UpdateShop(string actionType, ShopDto model)
        {
            try
            {
                if (string.Equals(actionType, "Delete", StringComparison.OrdinalIgnoreCase))
                {
                    if (model == null || model.Id == Guid.Empty) throw new ArgumentException("缺少 Id");
                    int affectedDelete = _db.DoSQL("Delete", "Shop", id: model.Id.ToString());
                    return Json(new { ok = affectedDelete > 0, rows = affectedDelete });
                }

                if (model == null) throw new ArgumentException("缺少資料");
                if (string.IsNullOrWhiteSpace(model.Name)) throw new ArgumentException("店家名稱必填");

                var data = new Dictionary<string, object>();
                if (string.Equals(actionType, "Create", StringComparison.OrdinalIgnoreCase))
                {
                    data["Id"] = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;
                    data["CreateDate"] = DateTime.UtcNow;
                }
                else if (model.Id == Guid.Empty) throw new ArgumentException("缺少 Id");

                data["Name"] = model.Name ?? (object)DBNull.Value;
                data["Phone"] = model.Phone ?? (object)DBNull.Value;
                data["Account"] = model.Account ?? (object)DBNull.Value;
                if (!string.IsNullOrWhiteSpace(model.Password) && model.Password.Length < 6)
                    throw new ArgumentException("密碼至少 6 碼");
                data["Password"] = model.Password ?? (object)DBNull.Value;
                data["Addr"] = model.Addr ?? (object)DBNull.Value;
                if (model.IsActive.HasValue) data["IsActive"] = model.IsActive.Value;
                data["UpdateDate"] = DateTime.UtcNow;

                int affected = string.Equals(actionType, "Create", StringComparison.OrdinalIgnoreCase)
                    ? _db.DoSQL("Create", "Shop", data: data)
                    : _db.DoSQL("Update", "Shop", id: model.Id.ToString(), data: data);

                return Json(new { ok = affected > 0, rows = affected, id = data.ContainsKey("Id") ? data["Id"] : model.Id });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, error = ex.Message });
            }
        }
        #endregion

        // ===== DTO：Table =====
        public class TableDto
        {
            public Guid Id { get; set; }
            public Guid ShopId { get; set; }
            public int Number { get; set; }
            public string Zone { get; set; }
            public bool? IsActive { get; set; }
        }

        #region Table
        // GET: /Home/GetTable?onlyActive=true
        [HttpGet]
        public JsonResult GetTable(bool? onlyActive)
        {
            var sql = @"
        SELECT t.Id, t.ShopId, s.Name AS ShopName, t.Number, t.Zone, t.IsActive, t.CreateDate, t.UpdateDate
        FROM dbo.[Table] AS t
        LEFT JOIN dbo.Shop AS s ON t.ShopId = s.Id
        WHERE (@OnlyAct IS NULL OR t.IsActive=@OnlyAct)
        ORDER BY s.Name ASC, t.Number ASC";
            var dt = _db.Query(sql, new Dictionary<string, object>
            {
                ["OnlyAct"] = (object)onlyActive ?? DBNull.Value
            });
            return Json(BackstageSQL.ToList(dt), JsonRequestBehavior.AllowGet);
        }

        // POST: /Home/UpdateTable
        // body: actionType=Create|Update|Delete + model.Id / model.Number / model.IsActive
        [HttpPost]
        public JsonResult UpdateTable(string actionType, TableDto model)
        {
            try
            {
                if (string.Equals(actionType, "Delete", StringComparison.OrdinalIgnoreCase))
                {
                    if (model == null || model.Id == Guid.Empty) throw new ArgumentException("缺少 Id");
                    int rowsAffected = _db.DoSQL("Delete", "Table", id: model.Id.ToString());
                    return Json(new { ok = rowsAffected > 0, rows = rowsAffected });
                }

                if (model == null) throw new ArgumentException("缺少資料");
                if (model.ShopId == Guid.Empty) throw new ArgumentException("桌號需指定店家");
                if (model.Number <= 0) throw new ArgumentException("桌號必須為正整數");
                var data = new Dictionary<string, object>();

                if (string.Equals(actionType, "Create", StringComparison.OrdinalIgnoreCase))
                {
                    data["Id"] = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;
                    data["CreateDate"] = DateTime.UtcNow;
                }
                else
                {
                    if (model.Id == Guid.Empty) throw new ArgumentException("缺少 Id");
                }

                data["ShopId"] = model.ShopId;
                data["Number"] = model.Number;
                data["Zone"] = string.IsNullOrWhiteSpace(model.Zone) ? (object)DBNull.Value : model.Zone.Trim();
                if (model.IsActive.HasValue) data["IsActive"] = model.IsActive.Value;
                data["UpdateDate"] = DateTime.UtcNow;

                int rows = string.Equals(actionType, "Create", StringComparison.OrdinalIgnoreCase)
                    ? _db.DoSQL("Create", "Table", data: data)
                    : _db.DoSQL("Update", "Table", id: model.Id.ToString(), data: data);

                return Json(new { ok = rows > 0, rows, id = data.ContainsKey("Id") ? data["Id"] : model.Id });
            }
            catch (Exception ex)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, error = ex.Message });
            }
        }
        #endregion

    }

    // ===== DTOs =====
    public class LoginRequest
    {
        public string Account { get; set; }
        public string Password { get; set; }
    }

    public class OrderItemRequest
    {
        public Guid MealId { get; set; }
        public int Quantity { get; set; }
        public string Notes { get; set; }
    }

    public class CreateOrderRequest
    {
        public string OrderType { get; set; }
        public Guid? TableId { get; set; }
        public Guid? ShopId { get; set; }
        public string Notes { get; set; }
        public List<OrderItemRequest> Items { get; set; }
    }

    public class ReceivingOrderDto
    {
        public Guid Id { get; set; }
        public Guid? ShopId { get; set; }
        public string ShopName { get; set; }
        public string OrderType { get; set; }
        public Guid? TableId { get; set; }
        public int? TableNumber { get; set; }
        public string TableZone { get; set; }
        public string TakeoutCode { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Notes { get; set; }
        public List<ReceivingOrderItemDto> Items { get; set; }
    }

    public class ReceivingOrderItemDto
    {
        public Guid Id { get; set; }
        public Guid MealId { get; set; }
        public string MealName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string Notes { get; set; }
    }

    public class UpdateOrderStatusRequest
    {
        public Guid OrderId { get; set; }
        public string Status { get; set; }
    }

    public class MealDto
    {
        public Guid Id { get; set; }
        public Guid ShopId { get; set; }
        public string Name { get; set; }
        public decimal? Money { get; set; }
        public bool? IsActive { get; set; }
        public string Element { get; set; }
        public string Table { get; set; }  // 若不用可忽略
        public string Classification { get; set; }
        public Guid? CategoryId { get; set; }
    }

    public class ComboDto
    {
        public Guid Id { get; set; }
        public Guid ShopId { get; set; }
        public string Name { get; set; }
        public string ComboMeal { get; set; }
        public decimal? Money { get; set; }
        public bool? IsActive { get; set; }
    }

    public class MealCategoryDto
    {
        public Guid Id { get; set; }
        public Guid ShopId { get; set; }
        public string Name { get; set; }
        public int? SortOrder { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ShopDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string Addr { get; set; }
        public bool? IsActive { get; set; }
    }
}
