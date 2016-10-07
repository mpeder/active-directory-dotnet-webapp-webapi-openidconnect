﻿//----------------------------------------------------------------------------------------------
//    Copyright 2014 Microsoft Corporation
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//----------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

// The following using statements were added for this sample.
using System.Configuration;
using System.Threading.Tasks;
using System.Security.Claims;
using TodoListWebApp.Utils;
using TodoListWebApp.Models;
using Microsoft.Owin.Security.OpenIdConnect;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Owin.Security;
using Microsoft.Graph;
using System.Text;

namespace TodoListWebApp.Controllers
{
    [Authorize]
    public class ValuesController : Controller
    {
        private string todoListResourceId = "https://damcodomain.onmicrosoft.com/apiappmulti"; //ConfigurationManager.AppSettings["todo:TodoListResourceId"];
        private string todoListBaseAddress = "http://multitenantdamcoauthpoc.azurewebsites.net/"; // ConfigurationManager.AppSettings["todo:TodoListBaseAddress"];
        private const string TenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";
        private static string clientId = ConfigurationManager.AppSettings["ida:ClientId"];
        private static string appKey = ConfigurationManager.AppSettings["ida:AppKey"];
        private string graphResourceId = "https://graph.microsoft.com/";
        //
        // GET: /TodoList/
        public async Task<ActionResult> Index()
        {
            AuthenticationResult result = null;


            try
            {
                string userObjectID = ClaimsPrincipal.Current.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value;
                AuthenticationContext authContext = new AuthenticationContext(Startup.Authority, new NaiveSessionCache(userObjectID));
                ClientCredential credential = new ClientCredential(clientId, appKey);
                result = await authContext.AcquireTokenSilentAsync(todoListResourceId, credential, new UserIdentifier(userObjectID, UserIdentifierType.UniqueId));

                //
                // Retrieve the user's To Do List.
                //
                HttpClient client = new HttpClient();
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, todoListBaseAddress + "/api/values");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
                HttpResponseMessage response = await client.SendAsync(request);

                HttpRequestMessage request2 = new HttpRequestMessage(HttpMethod.Get, todoListBaseAddress + "/api/auth");
                request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
                HttpResponseMessage response2 = await client.SendAsync(request2);

                var graphResult = await authContext.AcquireTokenSilentAsync(graphResourceId, credential, new UserIdentifier(userObjectID, UserIdentifierType.UniqueId));

                var graphServiceClient = new GraphServiceClient(
                    new DelegateAuthenticationProvider(
                    (requestMessage) =>
                    {
                        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", graphResult.AccessToken);

                        return Task.FromResult(0);
                    }));

                string graphinfo = string.Empty;

                try
                {
                   
                    var user= await graphServiceClient
                         .Me
                         .Request()
                         .Select("displayName")
                         .GetAsync();
                    graphinfo += "Display name = " + user.DisplayName;

                    //var usergroupsHttpReqMsg =  graphServiceClient.Me.GetMemberGroups(true).Request().GetHttpRequestMessage();
                    //usergroupsHttpReqMsg.Headers.Authorization = new AuthenticationHeaderValue("bearer", graphResult.AccessToken);
                    //usergroupsHttpReqMsg.Method = HttpMethod.Post;
                    //usergroupsHttpReqMsg.Content = new StringContent("{\"securityEnabledOnly\":false}", Encoding.UTF8, "application/json");
                    
                    //var responseGroups = await graphServiceClient.HttpProvider.SendAsync(usergroupsHttpReqMsg);
                    
                    //var getMemberGroupsRequestBuilder = graphServiceClient.Me.GetMemberGroups();
                    //var getMemberGroupsRequest = getMemberGroupsRequestBuilder.Request() as DirectoryObjectGetMemberGroupsRequest;
                    //var respoGroups = await getMemberGroupsRequest.SendRequestAsync();
                    //userGroups
                    //graphServiceClient.Me.Request().;

                }
                catch (Exception exp)
                {
                    string ex = exp.ToString();
                }
                //
                // Return the To Do List in the view.
                //
                if (response.IsSuccessStatusCode)
                {
                    List<Dictionary<String, String>> responseElements = new List<Dictionary<String, String>>();
                    JsonSerializerSettings settings = new JsonSerializerSettings();
                    String responseString = await response.Content.ReadAsStringAsync();

                    List<Dictionary<String, String>> responseElements2 = new List<Dictionary<String, String>>();
                    JsonSerializerSettings settings2 = new JsonSerializerSettings();
                    String responseString2 = await response2.Content.ReadAsStringAsync();

                    //responseElements = JsonConvert.DeserializeObject<List<Dictionary<String, String>>>(responseString, settings);
                    //foreach (Dictionary<String, String> responseElement in responseElements)
                    //{
                    //    TodoItem newItem = new TodoItem();
                    //    newItem.Title = responseElement["Title"];
                    //    newItem.Owner = responseElement["Owner"];
                    //    itemList.Add(newItem);
                    //}
                    ViewBag.Response = responseString + responseString2;
                    return View();
                }
                else
                {
                    //
                    // If the call failed with access denied, then drop the current access token from the cache, 
                    //     and show the user an error indicating they might need to sign-in again.
                    //
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        var todoTokens = authContext.TokenCache.ReadItems().Where(a => a.Resource == todoListResourceId);
                        foreach (TokenCacheItem tci in todoTokens)
                            authContext.TokenCache.DeleteItem(tci);

                        ViewBag.ErrorMessage = "UnexpectedError";
                        TodoItem newItem = new TodoItem();
                        newItem.Title = "(No items in list)";
                        //itemList.Add(newItem);
                        //return View(itemList);
                        ViewBag.Response = "Unauthorized";
                        return View();
                    }
                }
            }
            catch (AdalException ee)
            {
                if (Request.QueryString["reauth"] == "True")
                {
                    //
                    // Send an OpenID Connect sign-in request to get a new set of tokens.
                    // If the user still has a valid session with Azure AD, they will not be prompted for their credentials.
                    // The OpenID Connect middleware will return to this controller after the sign-in response has been handled.
                    //
                    HttpContext.GetOwinContext().Authentication.Challenge(
                        new AuthenticationProperties(),
                        OpenIdConnectAuthenticationDefaults.AuthenticationType);
                }

                //
                // The user needs to re-authorize.  Show them a message to that effect.
                //
                TodoItem newItem = new TodoItem();
                newItem.Title = "(Sign-in required to view to do list.)";
                //  itemList.Add(newItem);
                ViewBag.ErrorMessage = "AuthorizationRequired";
                return View();
            }


            //
            // If the call failed for any other reason, show the user an error.
            //
            return View("Error");

        }


    }
}