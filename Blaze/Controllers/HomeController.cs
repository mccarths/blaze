﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using Blaze.Models;
using NLog;

namespace Blaze.Controllers
{
    public interface ICommandWrapper
    {
        bool Match(string url);
        string ProcessContent(HttpRequestBase request, string ipAddress, string accountName, string content);
    }

    public class LoginLoggingCommandWrapper : ICommandWrapper
    {
        private static readonly Logger Log = LogManager.GetLogger("Blaze.Login");

        public bool Match(string url)
        {
            return url == "users/me.json";
        }

        public string ProcessContent(HttpRequestBase request, string ipAddress, string accountName, string content)
        {            
            dynamic obj = Newtonsoft.Json.JsonConvert.DeserializeObject(content);
            Log.Info("login on account: {0}. email: {1}. IP: {2}. Agent: {3}", accountName,
                     obj.user != null ? obj.user.email_address : "(unknown)", 
                     ipAddress, request.UserAgent);
            return content;
        }
    }

    public class HomeController : Controller
    {
        private readonly IList<ICommandWrapper> commandWrappers;
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public HomeController(IList<ICommandWrapper> commandWrappers)
        {
            this.commandWrappers = commandWrappers;
        }

        public HomeController()
        {
            commandWrappers = new List<ICommandWrapper>() {new LoginLoggingCommandWrapper()};
        }

        public ActionResult Index()
        {
            Log.Info("Home Page Visitor. Referrer: {0}, IP Address: {1}. Agent: {2}", Request.UrlReferrer, GetIPAddress(Request), Request.UserAgent);
            return View();
        }
        //
        // GET: /Home/

        public ActionResult Chat(string accountName)
        {
            ViewBag.AccountName = accountName;
            ViewBag.Stealth = Convert.ToBoolean(ConfigurationManager.AppSettings["Stealth"] ?? "true")
                                  ? "true"
                                  : "false";
            var model = new HomeModel
                            {
                            };
            return View("Chat",model);
        }

        public ActionResult Proxy(string account, string url, string auth)
        {
            ++++
            string fullUrl = string.Format("https://{0}.campfirenow.com/{1}?{2}", account, url, Request["QUERY_STRING"]);
            var request = (HttpWebRequest) WebRequest.Create(fullUrl);
            request.Method = Request.HttpMethod;
            request.ContentType = Request.ContentType;
            request.ContentLength = Request.ContentLength;
            if (!string.IsNullOrEmpty(auth))
                request.Headers["Authorization"] = "Basic  " + auth;
            else
                request.Headers["Authorization"] = Request.Headers["Authorization"];
            request.Accept = Request.Headers["Accept"];

            if (Request.HttpMethod == "POST" || Request.HttpMethod == "PUT")
            {
                var inStream = Request.InputStream;
                var outStream = request.GetRequestStream();
                inStream.CopyTo(outStream);
                outStream.Close();
            }
            try
            {
                var response = (HttpWebResponse) request.GetResponse();
                Response.StatusCode = (int) response.StatusCode;
                var reader = new StreamReader(response.GetResponseStream());
                var data = reader.ReadToEnd();
                data = commandWrappers
                    .Where(commandWrapper => commandWrapper.Match(url))
                    .Aggregate(data, (current, commandWrapper) => commandWrapper.ProcessContent(Request,GetIPAddress(Request), account, current));
                return Content(data, response.ContentType);
            } catch (WebException ex)
            {
                return HandleWebException(fullUrl, ex);
            }
        }

        private static string GetIPAddress(HttpRequestBase request)
        {
            string ip = request.ServerVariables["HTTP_X_FORWARDED_FOR"];
            if (!String.IsNullOrEmpty(ip))
            {
                // sometimes HTTP_X_FORWARDED_FOR returns multiple IP's
                string[] ipRange = ip.Split(',');
                ip = ipRange[ipRange.Length - 1];
            }
            else
                ip = request.ServerVariables["REMOTE_ADDR"];
            return ip;
        }

        public ActionResult Recent(string account, string url)
        {
            string fullUrl = string.Format("https://{0}.campfirenow.com/{1}?{2}", account, url, Request["QUERY_STRING"]);
            var request = (HttpWebRequest)WebRequest.Create(fullUrl);
            request.Method = "GET";
            request.ContentType = "application/json";
            request.Headers["Authorization"] = Request.Headers["Authorization"];
            try
            {
                var response = (HttpWebResponse) request.GetResponse();
                var reader = new StreamReader(response.GetResponseStream());
                var data = reader.ReadToEnd();
                dynamic obj = Newtonsoft.Json.JsonConvert.DeserializeObject(data);
                var processor = new MessageProcessor();
                foreach (var msg in obj.messages)
                {
                    msg.parsed_body = processor.ProcessMessage(Convert.ToString(msg.body));
                }
                string result = Newtonsoft.Json.JsonConvert.SerializeObject(obj);
                return Content(result, "application/json");
            }
            catch (WebException ex)
            {
                return HandleWebException(fullUrl,ex);
            }
        }

        private ActionResult HandleWebException(string fullUrl, WebException ex)
        {
            if (ex.Response == null)
            {
                Log.ErrorException(string.Format("Web Exception received requesting URL: {0}", fullUrl), ex);
                Response.StatusCode = 503;
                return null;
            }
            var response = ((HttpWebResponse) ex.Response);
            Response.StatusCode = (int) response.StatusCode;
            Log.ErrorException(string.Format("Response Code : {0} received requesting URL: {1}", response.StatusCode, fullUrl), ex);
            return new FileStreamResult(response.GetResponseStream(), response.ContentType);
        }

        public ActionResult GetFile(string account, string auth, string url)
        {
            string fullUrl = string.Format("https://{0}.campfirenow.com/{1}?{2}", account, url, Request["QUERY_STRING"]);
            var request = (HttpWebRequest)WebRequest.Create(fullUrl);
            request.Method = "GET";
            request.Headers["Authorization"] = "Basic  " + auth;
            try
            {
                var response = request.GetResponse();
                return new FileStreamResult(response.GetResponseStream(), response.ContentType);
            } catch (WebException ex)
            {
                return HandleWebException(fullUrl, ex);
            }
        }
    }
}
