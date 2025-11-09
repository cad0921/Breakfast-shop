using System;
using System.Collections.Generic;
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

        public ActionResult Index() { return View(); }
        public ActionResult Backstage() { return View(); }
        public ActionResult Order() {
            return View();
        }
        public ActionResult Login()
        {
            return View();
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
                    throw new ArgumentException("密碼至少 6 碼");

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
            public int Number { get; set; }
            public bool? IsActive { get; set; }
        }

        #region Table
        // GET: /Home/GetTable?onlyActive=true
        [HttpGet]
        public JsonResult GetTable(bool? onlyActive)
        {
            var sql = @"
        SELECT Id, Number, IsActive, CreateDate, UpdateDate
        FROM dbo.[Table]
        WHERE (@OnlyAct IS NULL OR IsActive=@OnlyAct)
        ORDER BY Number ASC";
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

                data["Number"] = model.Number;
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
