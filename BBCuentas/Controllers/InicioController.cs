﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace BBCuentas.Controllers
{
    public class InicioController : Controller
    {
        [Authorize(Roles = "Admin")]
        public ActionResult Inicio()
        {
            return View();
        }
    }
}