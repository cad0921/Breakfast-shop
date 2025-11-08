using System;
using System.Collections.Generic;
using System.Web.Mvc;
using breakfastshop.Models;

namespace breakfastshop.Controllers
{
    public class HomeController : Controller
    {
        private readonly BackstageSQL _db = new BackstageSQL();

        public ActionResult Index() { return View(); }
        public ActionResult Backstage() { return View(); }

        #region Meals
        [HttpGet]
        public JsonResult GetMeals(Guid? shopId, bool? onlyActive)
        {
            var sql = @"SELECT Id, ShopId, Name, Money, IsActive, Element, CreateDate, UpdateDate
                        FROM dbo.Meals
                        WHERE (@ShopId IS NULL OR ShopId=@ShopId)
                          AND (@OnlyAct IS NULL OR IsActive=@OnlyAct)
                        ORDER BY Name ASC";
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

        #region Combo
        [HttpGet]
        public JsonResult GetCombo(Guid? shopId, bool? onlyActive)
        {
            var sql = @"SELECT Id, ShopId, ComboMeal, Money, IsActive, CreateDate, UpdateDate
                        FROM dbo.Combo
                        WHERE (@ShopId IS NULL OR ShopId=@ShopId)
                          AND (@OnlyAct IS NULL OR IsActive=@OnlyAct)
                        ORDER BY CreateDate DESC";
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
                data["ComboMeal"] = model.ComboMeal ?? (object)DBNull.Value;
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
            var sql = @"SELECT Id, Name, Phone, Addr, IsActive, CreateDate, UpdateDate
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
    public class MealDto
    {
        public Guid Id { get; set; }
        public Guid ShopId { get; set; }
        public string Name { get; set; }
        public decimal? Money { get; set; }
        public bool? IsActive { get; set; }
        public string Element { get; set; }
        public string Table { get; set; }  // 若不用可忽略
    }

    public class ComboDto
    {
        public Guid Id { get; set; }
        public Guid ShopId { get; set; }
        public string ComboMeal { get; set; }
        public decimal? Money { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ShopDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Addr { get; set; }
        public bool? IsActive { get; set; }
    }
}
