using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace MvcWebRole.Controllers
{
    public class DocumentationController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult Subscriptions_Subscribers()
        {
            return View();
        }

        public ActionResult Subscriptions_Apps()
        {
            return View();
        }

        public ActionResult Subscriptions_Logs()
        {
            return View();
        }

        public ActionResult Subscriptions_Subscribe()
        {
            return View();
        }

        public ActionResult Subscriptions_Verify()
        {
            return View();
        }

        public ActionResult Snapshots_Latest()
        {
            return View();
        }

        public ActionResult Snapshots_Log()
        {
            return View();
        }

        public ActionResult Proofs_App()
        {
            return View();
        }

        public ActionResult Proofs_App_Log()
        {
            return View();
        }

        public ActionResult Proofs_Proofs_Query()
        {
            return View();
        }

        public ActionResult Proofs_Proofs_Audit()
        {
            return View();
        }
    }
}
