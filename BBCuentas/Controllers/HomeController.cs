﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace BBCuentas.Controllers
{
    public class HomeController : System.Web.Mvc.Controller
    {
        [Authorize(Roles = "User")]
        public ActionResult Index(string parametro)
        {
            ViewData["Message"] = parametro;
            return View();
        }
    }
}