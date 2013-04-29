using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace MvcWebRole
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            /*config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );*/

            //
            // NOTE: The dash is needed because app names cannot have it and therefore cannot match
            //
            config.Routes.MapHttpRoute(
                name: "ProofsQuery",
                routeTemplate: "api/proofs/proofs-query",
                defaults: new
                {
                    controller = "Proofs",
                    action = "Query"
                }
            );

            //
            // NOTE: Dash is also needed here for reasons above
            //
            config.Routes.MapHttpRoute(
                name: "ProofsAudit",
                routeTemplate: "api/proofs/proofs-audit",
                defaults: new
                {
                    controller = "Proofs",
                    action = "Audit"
                }
            );

            config.Routes.MapHttpRoute(
                name: "ProofsAppAndLogAPI",
                routeTemplate: "api/proofs/{appName}/{logName}",
                defaults: new 
                {
                    controller = "Proofs",
                    action = "GetProofs"
                }
            );
            
            config.Routes.MapHttpRoute(
                name: "ProofsAppAPI",
                routeTemplate: "api/proofs/{appName}",
                defaults: new
                {
                    controller = "Proofs",
                    action = "GetProofs",
                    logName = ""
                }
            );

            config.Routes.MapHttpRoute(
                name: "ActionApi",
                routeTemplate: "api/{controller}/{action}/{id}",
                defaults: new { id = RouteParameter.Optional }
                );
            // Uncomment the following line of code to enable query support for actions with an IQueryable or IQueryable<T> return type.
            // To avoid processing unexpected or malicious queries, use the validation settings on QueryableAttribute to validate incoming queries.
            // For more information, visit http://go.microsoft.com/fwlink/?LinkId=279712.
            //config.EnableQuerySupport();
        }
    }
}