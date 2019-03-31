using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using System.Web.SessionState;
using TouchPoints.Cms.Core.Annotation;
using TouchPoints.Cms.Core.Enums;
using tcchc = TouchPoints.Cms.Core.HelperClasses;
using TouchPoints.Cms.Core.HelperClasses.AdvisorProfile;
using TouchPoints.Cms.Core.HelperClasses.Seo;
using TouchPoints.Cms.Core.Managers;
using TouchPoints.Cms.Core.Managers.AdvisorProfile;
using TouchPoints.Cms.Core.Managers.AgencyProfile;
using TouchPoints.Cms.Core.Managers.SupplierMarketingOptions;
using TouchPoints.Cms.Core.Managers.ConsumerNotifications;
using TouchPoints.Cms.Core.Models.Lookup;
using TouchPoints.Cms.Core.Models.View;
using TouchPoints.Cms.Core.Models.View.SupplierMarketingOptions;
using TouchPoints.Cms.Core.ModelSecurity;
using TouchPoints.Framework.DataStructures;
using TouchPoints.Framework.Logging;
using TouchPoints.Framework.Security;
using TouchPoints.Framework.Utilities;
using TouchPoints.Security.Authentication.Clients;
using TouchPoints.Services.HelperClasses.Cobrand;
using TouchPoints.Cms.Core.Services.Managers;
using TouchPoints.Services.ProductCatalog.Core;
using TouchPoints.Services.ProductCatalog.Core.Search;
using TouchPoints.Services.Security;
using Virtuoso.SSO.Client.Authentication.Clients;
using SecurityRole = TouchPoints.Framework.UserAccount.SupportingClasses.SecurityRole;
using TouchPoints.Cms.Core.Models.Transport;
using TouchPoints.Cms.Core.Models.Data;
using TouchPoints.Services.ProductCatalog.Core.Search.Managers;
using TouchPoints.Services.ProductCatalog.Core.DataEntities;
using TouchPoints.Cms.Core.Models;
using TouchPoints.Cms.Core.Models.ActionLog;
using ActionLogManager = TouchPoints.Cms.Core.Managers.ActionLogManager;
using TouchPoints.Cms.Core.HelperClasses;
using TouchPoints.Services.DataAccess.Databases.Composer;
using TouchPoints.Services.DataAccess.Models.Composer;
using TouchPoints.Services.DataAccess.Models.ProductDetail.Advisor;
using TouchPoints.Services.DataAccess.Models.ProductDetail.Common;
using TouchPoints.Services.Managers;
using TouchPoints.Services.Managers.AdvisorProfile;
using TouchPoints.Services.Managers.Dynamics;
using AdvisorContactInfoManager = TouchPoints.Cms.Core.Services.Managers.AdvisorProfile.AdvisorContactInfoManager;
using TouchPoints.Cms.Core.Services.Managers.AdvisorProfile;
using TouchPoints.Framework.Caching;
using SearchResults = TouchPoints.Cms.Core.Models.Search.SearchResults;

namespace TouchPoints.Cms.Core.Controllers
{
    [SessionState(SessionStateBehavior.ReadOnly)]
	public class AdvisorsController : SecuredControllerBase
    {
        public SortTypes DefaultSortType
        {
            get { return FederatedLoginClient.User.IsCobranded ? SortTypes.AdvisorNameAsc : SortTypes.LeadGenDesc; }
        }

        [HttpGet]
        [CobrandValidate]
        public ActionResult Search()
        {
            return DoSearch();
        }

        /// <summary>
        /// Search with a specific page.  Mainly used by crawlers.
        /// </summary>
        /// <param name="page"></param>
        /// <returns></returns>
        [HttpGet]
        [CobrandValidate]
        public ActionResult SearchWithPage(int page)
        {
            return DoSearch(page);
        }

        private ActionResult DoSearch(int? page = null)
        {
            string uri = AdvisorHelper.GetValidAdvisorUri();

            if (new AdvisorDetailManager().ShouldRedirectToMyAdvisor() && uri.IsNotNullOrEmpty() && Request.Url != null)
            {
                return Redirect(uri + Request.Url.Query);
            }
			
            if (IsPhone)
            {
                // Mobile
                var searchManager = new SearchManager();
                var options = new SearchOptions { SearchType = SearchTypes.Advisor.ToString() };
                var results = searchManager.GetFacetResultsForMobile(options);
                ViewBag.CurrentPage = page ?? 1;
                return View("Search", results);
            }
            else
            {
                // Desktop
                SearchResults results;
                if (page == null)
                {
                    results = SearchResults();
                    return View("Search", results);
                }
                results = SearchResults(page.Value);
                return results.Advisor.Results.IsNullOrEmpty() ? PageNotFoundActionResult() : View("Search", results);
            }
        }

        [CobrandValidate]
        public ActionResult Index(int id = 0)
        {
            if (id == 0)
            {
                return PageNotFoundActionResult();
            }
            if (SecurityRules.IsConsumerViewingOtherAdvisorProfile(id))
			{
                return Request.Url != null ? Redirect(AdvisorHelper.GetMyAdvisorUri + Request.Url.Query) : PageForbiddenActionResult();
			}
            string url = SeoHelper.ValidateSlug(id, Request.RawUrl);
            if (!string.IsNullOrWhiteSpace(url))
            {
                return new RedirectResult(url, true);
                
            }
            var manager = new AdvisorViewManager();
            if (string.IsNullOrEmpty(ViewBag.canonicalUrl) && Request.Url != null)
            {
                ViewBag.canonicalUrl = TouchPoints.Services.HelperClasses.Url.BuildCanonicalUrl(Request.Url, Config.AppSettings.CanonicalScheme, Config.AppSettings.CanonicalHostname);
            }

            AdvisorDetailViewModel viewModel = manager.BuildAdvisorViewModel(id);
         
            if (viewModel == null)
            {
                return PageNotFoundActionResult();
            }
            viewModel.InitializeBreadcrumb();


            viewModel.LoadSecurity("advisors-index", viewModel);
            GetEditBadgeSettings(viewModel);
            return View(viewModel);
        }

        public ActionResult FindAnAdvisor()
        {
            if (FederatedLoginClient.User.IsCobranded)
            {
                return Redirect(Cobranding.AdjustCobrandedPageUri("/advisors"));
            }
            return View();
        }

        [AjaxPost]
        public ActionResult GetAdvisorCobrandView(AdvisorCobrandInfoViewModel options)
        {
            var viewModel = new AdvisorCobrandInfoViewModel
            {
                //AdvisorCobrandInfo = Advisor.CobrandedAdvisorSettingsGet(options.AdvisorCobrandInfo)
                AdvisorCobrandInfo = PrivateLabelManager.GetAdvisorCobrandInfo(options.AdvisorID, options.AdvisorCompanyMeid)
            };
            viewModel.LoadSecurity("advisors-index", viewModel);
            ActionResult view = PartialView(options.AdvisorSettingsPartial, viewModel);
            return view;
        }

        [AjaxPost]
        public ActionResult SetAdvisorCobrandView(AdvisorCobrandInfoViewModel options)
        {
            var cobrandedSiteNameErrors = ValidateCobrandedSiteName(options);
            var cobrandedLogoErrors = ValidateCustomLogo(options);
            var returnValue = "";
            if ( ModelState.IsValid && cobrandedSiteNameErrors.IsNullOrEmpty() && cobrandedLogoErrors.IsNullOrEmpty()) 
            {
                var savedOk = AdvisorCobrandAndSettingsManager.SaveCobrandedInfo(options.AdvisorCobrandInfo, FederatedLoginClient.User.UserMasterEntityId);
                return new HttpStatusCodeResult( savedOk ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            }
            // REVIEW: this is a non-standard way to handle validation errors. Ripe for refactoring as time permits.
            // We should set up validation attributes in the incoming model,
            // and rely upon Html.ValidationMessage's in the cshtml template, injecting error text into the markup.
            cobrandedSiteNameErrors.ForEach(error => ModelState["options.CobrandedSiteName"].Errors.Add(error));
            cobrandedLogoErrors.ForEach(error => ModelState["options.LogoData"].Errors.Add(error));

            // validation error -- round-trip the incoming model w/ the validation error list injected
            options.SavedStatus = returnValue;

            // need to refresh a few dictionaries which are not posted to this method, since they are critical to the UI
            var candidatesAndEditors = CobrandingManager.GetCandidatesAndEditors(options.AdvisorCompanyMeid, options.SelectedPrivateLabelEditorIds);
            options.PrivateLabelEditorCandidates = candidatesAndEditors.Item1;
            options.SelectedPrivateLabelEditors = candidatesAndEditors.Item2;
            
            var view = PartialView(options.AdvisorSettingsPartial, options);
            Response.StatusCode = (int) HttpStatusCode.PartialContent;
            return view;
        }

        [AjaxPost]
        public int ValidateAdvisorSiteName(AdvisorCobrandInfo options)
        {
            if (string.IsNullOrEmpty(options.CobrandedSiteName))
                return 1;
            return Advisor.CobrandedSiteNameCheck(options);
        }

        private static List<string> ValidateCobrandedSiteName(AdvisorCobrandInfoViewModel options)
        {
            return CobrandingManager.ValidateCobrandedSiteName(options.AdvisorID, options.CobrandedSiteName, options.PrivateLabelType, options.PrivateLabelUrlType);
        }

        private static List<string> ValidateCustomLogo(AdvisorCobrandInfoViewModel options)
        {
            var errors = new List<string>();

            // only check for logo if it is a PL site, and some logo operation was performed
            if ( options.PrivateLabelType.ToLowerInvariant() != "none" && options.LogoOperation.IsNotNullOrEmpty())
            {
                switch( options.LogoOperation.ToLowerInvariant())
                {
                    case "missing":
                        errors.Add("A custom logo file must be uploaded");
                        break;
                    case "personalization":
                        break;
                    case "add":
                        // REFACTOR? assume that all rules regarding content (mime type, image sizes) are handled on the client
                        break;
                    default:
                        break;
                }
                
            }
            return errors;
        }

        [HttpGet]
        public ActionResult AdvisorSettings(int id, bool isEditMode)
        {

            var manager = new AdvisorViewManager();
            var vm = manager.GetAdvisorDetailViewModel(id, isEditMode);
            if (vm == null || null == vm.Settings)
            {
                return PageNotFoundActionResult();
            }

            // TODO: load security with the non-hack way
            vm.LoadSecurity("advisors-index", vm);

            return PartialView(isEditMode ? "EditorTemplates/Admin.AdvisorSettingsContent" : "DisplayTemplates/Admin.AdvisorSettings", vm.Settings);
        }

        [AjaxGet]
        public ActionResult GetLookupData(AjaxOptions options)
        {
            var jsonResult = string.Empty;
            var attr = options.OperationEnum.Attribute<LookupDataAttribute>(null);
            if (attr != null && attr.GetMethod != null)
            {
                var parameters = options.OperationEnum == LookupDataTypes.PlacesVisited || options.OperationEnum == LookupDataTypes.SpecialtyDestinations
                                     ? new object[] { options, ProfileType.Advisor }
                                     : new object[] { options };
                var result = attr.GetMethod.Invoke(null, parameters);

                if (result != null)
                    jsonResult = Framework.Utilities.Json.Serialize(result);
            }
            return Content(jsonResult);
        }

        [AjaxPost]
        public ActionResult SetLookupData(AjaxOptions options)
        {
            var jsonResult = string.Empty;
            var attr = options.OperationEnum.Attribute<LookupDataAttribute>(null);
            if (attr != null && attr.SetMethod != null)
            {
                var result = attr.SetMethod.Invoke(null, new object[] { options, ProfileType.Advisor });

                if (result != null)
                    jsonResult = Framework.Utilities.Json.Serialize(result);
            }

            return Content(jsonResult);
        }

        public ActionResult LaunchProductionReport()
        {
            var targetUrl = "/Reports/ProductionReport.aspx";
            var urlWithAuthToken = ComposerSSOManager.GenerateRequestUri(targetUrl, FederatedLoginClient.User);
            return Redirect(urlWithAuthToken);
        }
        [AjaxPost]
        public ActionResult LaunchVirtuosoReports(AjaxOptions options)
        {
            var auditItem = new SecurityAuditItem
            {
                AuthKey = FederatedLoginClient.User.AuthKey,
                EventType = SecurityAuditEventType.SystemTransfer,
                FederatedUserId = FederatedLoginClient.User.FederatedUserId,
                TimeStamp = DateTime.UtcNow,
                ExtensionItems = new SerializableDictionary<string, string>
                {
                    {"InitialSystem","VCOM"},
                    {"FinalSystem", "ComposerReports"}
                },
                Login = String.Empty,
                Ip = String.Empty,
                SessionId = string.Empty
               
            };
            FederatedLoginClient.LogAuditEvent(auditItem);

            return Content(ConfigurationManager.AppSettings["ComposerReportsLandingPage"]);
            //var targetUrl = "/BILaunchpad.aspx";
            //var urlWithAuthToken = ComposerSSOManager.GenerateRequestUri(targetUrl, FederatedLoginClient.User);
            //return Content(urlWithAuthToken);
        }

        [AjaxPost]
        public ActionResult LaunchVirtuosoTravelAcademy(AjaxOptions options)
        {
            var urlWithAuthParams = CornerstoneSSOManager.GenerateRequestUri(FederatedLoginClient.User);
            var auditItem = new SecurityAuditItem
            {
                AuthKey = FederatedLoginClient.User.AuthKey,
                EventType = SecurityAuditEventType.SystemTransfer,
                FederatedUserId = FederatedLoginClient.User.FederatedUserId,
                TimeStamp = DateTime.UtcNow,
                ExtensionItems = new SerializableDictionary<string, string>
                {
                    {"InitialSystem","VCOM"},
                    {"FinalSystem", "TravelAcademy"}
                },
                Login = String.Empty,
                Ip = String.Empty,
                SessionId = String.Empty,
            };
            FederatedLoginClient.LogAuditEvent(auditItem);
            return Content(urlWithAuthParams);
        }

        [AjaxPost]
        public ActionResult ContactInfoEditor(DataAccess.Models.ProductDetail.Advisor.AdvisorContactInfo model)
        {
            switch (model.ItemTypeToAdd)
            {
                case ProfileItemType.Phone:
                    model.PhoneNumbers = model.PhoneNumbers ?? new List<ProfilePhoneNumber>();
                    model.PhoneNumbers.Add(new ProfilePhoneNumber { Status = ProfileItemStatus.Insert });
                    break;
                case ProfileItemType.Email:
                    model.EmailAddresses = model.EmailAddresses ?? new List<ProfileEmailAddress>();
                    model.EmailAddresses.Add(new ProfileEmailAddress { Status = ProfileItemStatus.Insert });
                    break;
                case ProfileItemType.Website:
                    model.WebSites = model.WebSites ?? new List<ProfileWebSite>();
                    model.WebSites.Add(new ProfileWebSite { Status = ProfileItemStatus.Insert, EntityType = model.EntityType });
                    break;
                case ProfileItemType.Messenger:
                    model.Messengers = model.Messengers ?? new List<ProfileInstantMessenger>();
                    model.Messengers.Add(new ProfileInstantMessenger { Status = ProfileItemStatus.Insert, EntityType = model.EntityType });
                    break;
                case ProfileItemType.Address:
                    model.Addresses = model.Addresses ?? new List<ProfileAddress>();
                    model.Addresses.Add(new ProfileAddress { Status = ProfileItemStatus.Insert });
                    break;
                default:
                    ProfileAddressManager.DisambiguateAddressCity(model.PrimaryAddress, true);
                    ProfileAddressManager.DisambiguateAddressCities(model.Addresses, true);
                    this.ModelState.Clear();    // prevent Html helper from using unaltered (ambiguous) address cities
                    break;
            }

            return PartialView("EditorTemplates/ContactInfo", model);
        }

        [AjaxPost]
        public ActionResult SaveContactInfo(DataAccess.Models.ProductDetail.Advisor.AdvisorContactInfo model)
        {
            if (ModelState.IsValidField("AdvisorMasterEntityId"))
            {
                var isValid = AdvisorViewManager.ValidateProfileAddresses(model, ModelState);
                if (!isValid)
                {
                    var thePartial = PartialView("EditorTemplates/ContactInfo", model);
                    Response.StatusCode = (int)HttpStatusCode.PartialContent;
                    return thePartial;
                }
                else
                {
                    var success = AdvisorContactInfoManager.SaveContactInfo(FederatedLoginClient.User.UserMasterEntityId, model);
                    if (success)
                    {
                        ProfileAddressManager.UpdateListForNewCity(model.Addresses);
                        return new HttpStatusCodeResult(HttpStatusCode.OK);
                    }
                    else
                    {
                        return new HttpStatusCodeResult(HttpStatusCode.InternalServerError);
                    }
                }
            }
            else
            {
                // no MEID can't deal...
                return new HttpNotFoundResult();
            }
        }

        [AjaxPost]
        public ActionResult DeletePlacesVisited(int placesVisitedId)
        {
            return Content(Framework.Utilities.Json.Serialize(PlacesVisited.Delete(placesVisitedId, ProfileType.Advisor)));
        }

      //  [AjaxPost]
        public ActionResult ProductDetailVirtuosoAdvisorsPartial(RecommendedAdvisorFactors factors)
        {
            ICollection<AdvisorCardInfo> advisorCards = null;

            if (factors != null)
            {
                // here, we've already done the rules about 'does the user get to see the module'
                // this is response to click the button 
                var recommendedAdvisors = AdvisorHelper.RetrieveRecommendedAdvisors(factors);

                // TODO: make the view model the RecommendedAdvisorResults or new VM (TBD)
                if (recommendedAdvisors.NumberDestinationSpecialistsForCountry > 1 && factors.ProductLocationCountry.IsNotNullOrEmpty())
                {
                    ViewBag.ProductLocationCountry = factors.ProductLocationCountry.Split('|').First();
                }
                else if (recommendedAdvisors.NumberAdvisorsMatchingInterest > 1)
                {
                    ViewBag.ProductInterestType = factors.InterestType;
                }

                advisorCards = recommendedAdvisors.AdvisorCards;
            }

            // If this is an advisor email marketing cobranded page, don't show the "Find different advisor link"
            var userInfo = FederatedLoginClient.User;
            ViewBag.ShowFindDifferentAdvisorLink = !(userInfo.IsMarketingProgram && userInfo.CobrandedAdvisorInfo.AdvisorId > 0);
            return PartialView("Partial/_ProductDetail_RecommendedAdvisors", advisorCards);
        }

        [HttpGet]
        public ActionResult Personalization(int id, bool isEditMode)
        {
            var manager = new Managers.AdvisorDetailManager();
            var advisorViewModel = manager.GetAdvisorDetailInfo(id, isEditMode);
            if (advisorViewModel == null)
            {
                return PageNotFoundActionResult();
            }
            return Personalization(advisorViewModel, isEditMode);
        }

        private ActionResult Personalization(AdvisorDetailViewModel viewModel, bool isEditMode)
        {
            viewModel.LoadSecurity("advisors-index", viewModel);
            return PartialView(isEditMode ? "EditorTemplates/PersonalizationInfo" : "DisplayTemplates/PersonalizationInfo", viewModel);
        }

        [HttpGet]
        public ActionResult AdminInformation(int id, bool isEditMode)
        {
            var manager = new Managers.AdvisorDetailManager();
            var advisorViewModel = manager.GetAdvisorDetailInfo(id, isEditMode);
            if (advisorViewModel == null)
            {
                return PageNotFoundActionResult();
            }
            return AdminInformation(advisorViewModel, isEditMode);
        }

        private void GetEditBadgeSettings(AdvisorDetailViewModel viewModel)
        {
            if (viewModel == null || viewModel.Information == null)
            {
                return;
            }
            var mayEditEventBadge = viewModel.CanView(SecurityConstants.Advisor.EditEventBadge);
            viewModel.Information.MayEditBadge = mayEditEventBadge;
            viewModel.Information.Badge.MayEditCompany = viewModel.CanView(SecurityConstants.Advisor.EditEventBadgeCompanyField); 
            if (viewModel.Information.Badge != null)
            {
                viewModel.Information.Badge.ShowAgencyStaffInformationalMessage = !mayEditEventBadge;
            }
        }
        private ActionResult AdminInformation(AdvisorDetailViewModel viewModel, bool isEditMode)
        {
            viewModel.LoadSecurity("advisors-index", viewModel);

            if (viewModel.CanView(SecurityConstants.Advisor.AdminTab))
            {
                return PartialView(isEditMode ? "EditorTemplates/Admin.AdvisorInformation" : "DisplayTemplates/Admin.AdvisorInformation", viewModel.Information);
            }
            return new HttpStatusCodeResult(HttpStatusCode.Forbidden);
        }
        [HttpGet]
        public ActionResult AdminEventBadge(int id, bool isEditMode)
        {
            var manager = new Managers.AdvisorDetailManager();
            var advisorViewModel = manager.GetAdvisorDetailInfo(id, isEditMode);
            if (advisorViewModel == null)
            {
                return PageNotFoundActionResult();
            }
            return AdminEventBadge(advisorViewModel, isEditMode);
        }

        private ActionResult AdminEventBadge(AdvisorDetailViewModel viewModel, bool isEditMode)
        {
            viewModel.LoadSecurity("advisors-index", viewModel);

            if (viewModel.CanView(SecurityConstants.Advisor.AdminTab))
            {
                GetEditBadgeSettings(viewModel);
                return PartialView(isEditMode ? "EditorTemplates/Admin.EventBadge" : "DisplayTemplates/Admin.EventBadge", viewModel.Information);
            }
            return new HttpStatusCodeResult(HttpStatusCode.Forbidden);
        }

        [AjaxPost]
        public ActionResult AddAdvisorId(AdvisorInformationViewModel model)
        {
            model.ListAdvisorIds = model.ListAdvisorIds ?? new List<AdvisorIdClass>();
            var isValid = AdvisorViewManager.ValidateAdvisorIds(model as AdvisorInformationViewModel, ModelState);
            if (isValid)
                model.ListAdvisorIds.Add(new AdvisorIdClass {Status = ProfileItemStatus.Insert});

            return PartialView("EditorTemplates/Admin.AdvisorInformation", model);
        }

        [ValidateAntiForgeryToken]
        [AjaxPost]
        public ActionResult AdminEventBadgeUpdate(AdvisorInformationViewModel viewModel)
        {
            if (viewModel.Badge == null || !viewModel.Badge.DirtyFlag) //if nothing has changed don't need to go any further
            {
                return new HttpStatusCodeResult(HttpStatusCode.OK);
            }
            viewModel.LoadSecurity("advisors-admininformationupdate", viewModel);
            //ignore errors for non-badge related fields
            tcchc.ValidationHelper.IgnoreValidationExceptFor(ModelState, "Badge.Company", "Badge.Name", "Badge.NickName", "Badge.Location");

            if (ModelState.IsValid)
            {
                var success = AdvisorInfoManager.SaveEventBadgeInfo(FederatedLoginClient.User.UserMasterEntityId, viewModel.AdvisorInformation);
                return new HttpStatusCodeResult(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            }
            else
            {
                // Validation error(s) detected on the server side
                var partialView = PartialView("EditorTemplates/Admin.EventBadge", viewModel);
                Response.StatusCode = (int)HttpStatusCode.PartialContent;
                return partialView;
            }
        }

        [ValidateAntiForgeryToken]
        [AjaxPost]
        public ActionResult AdminInformationUpdate(AdvisorInformationViewModel viewModel)
        {
            viewModel.LoadSecurity("advisors-admininformationupdate", viewModel);

            // no validation on DBA if not IC
            var editIdType = viewModel.CanView(SecurityConstants.Advisor.EditAdvisorIdType);
            if (viewModel.AdvisorType.TypeId != AdvisorInfoManager.IndependentContractorTypeId && ModelState.ContainsKey("AdvisorDoingBusinessAs"))
            {
                ModelState["AdvisorDoingBusinessAs"].Errors.Clear();
            }

            // no validate on Type for self-edit
            if (!editIdType && ModelState.ContainsKey("AdvisorType.TypeId"))
            {
                ModelState["AdvisorType.TypeId"].Errors.Clear();
            }

            // no validate AdvisorID for self-edit
            if (!editIdType && viewModel.ListAdvisorIds.IsNotNullOrEmpty())
            {
                for (int i = 0; i < viewModel.ListAdvisorIds.Count; i++)
                {
                    var name = string.Format("ListAdvisorIds[{0}].Id", i);
                    if (ModelState.ContainsKey(name))
                    {
                        ModelState[name].Errors.Clear();
                    }
                }
            }

            if (ModelState.ContainsKey("SellingTravelSince"))
            {
                ModelState["SellingTravelSince"].Errors.Clear();
            }
            
            if (ModelState.ContainsKey("Badge.Company"))
            {
                ModelState["Badge.Company"].Errors.Clear();
            }
            if (ModelState.ContainsKey("Badge.Name"))
            {
                ModelState["Badge.Name"].Errors.Clear();
            }
            if (ModelState.ContainsKey("Badge.NickName"))
            {
                ModelState["Badge.NickName"].Errors.Clear();
            }
            if (ModelState.ContainsKey("Badge.Location"))
            {
                ModelState["Badge.Location"].Errors.Clear();
            }

            var isValid = ModelState.IsValid && AdvisorViewManager.ValidateAdvisorIds(viewModel, ModelState);
            if (isValid)
            {
                var userMasterEntityId = FederatedLoginClient.User.UserMasterEntityId;
                var success = AdvisorInfoManager.SaveAdminInfo(userMasterEntityId, viewModel.AdvisorInformation, editIdType);
                return new HttpStatusCodeResult(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            }
            else
            {
                // Validation error(s) detected on the server side
                var partialView = PartialView("EditorTemplates/Admin.AdvisorInformation", viewModel);
                Response.StatusCode = (int) HttpStatusCode.PartialContent;
                return partialView; 
            }
        }
		[HttpGet]
		public ActionResult AdminHotelBooking(int id, bool isEditMode)
		{
			var manager = new Managers.AdvisorDetailManager();
			var advisorViewModel = manager.GetAdvisorDetailInfo(id, isEditMode);
			if (advisorViewModel == null)
			{
				return PageNotFoundActionResult();
			}
			return AdminHotelBooking(advisorViewModel, isEditMode);
		}
		private ActionResult AdminHotelBooking(AdvisorDetailViewModel viewModel, bool isEditMode)
		{
			viewModel.LoadSecurity("advisors-index", viewModel);
			if (viewModel.CanView(SecurityConstants.Advisor.AdminTab))
			{
				return PartialView(isEditMode ? "EditorTemplates/Admin.BookingProgram" : "DisplayTemplates/Admin.BookingProgram", viewModel.HotelBooking);
			}
			return new HttpStatusCodeResult(HttpStatusCode.Forbidden);
		}

        [ValidateAntiForgeryToken]
		[AjaxPost]
		public ActionResult AdminHotelBookingUpdate(AdvisorHotelBookingViewModel viewModel)
		{
				if (ModelState.IsValid && viewModel.HotelBooking != null)
				{
					var success = AdvisorViewManager.SetHotelBookingInfo(viewModel.HotelBooking);
					return new HttpStatusCodeResult(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
				}
				else
				{
					// Validation error(s) detected on the server side
					var partialView = PartialView("EditorTemplates/Admin.BookingProgram", viewModel);
					Response.StatusCode = (int)HttpStatusCode.PartialContent;
					return partialView;
				}
		
		}

        [HttpGet]
        public ActionResult AdminSpecialties(int id, bool isEditMode)
        {
            var manager = new Managers.AdvisorDetailManager();
            var advisorViewModel = manager.GetAdvisorDetailInfo(id, isEditMode);
            if (advisorViewModel == null)
            {
                return PageNotFoundActionResult();
            }
            return AdminSpecialties(advisorViewModel, isEditMode);
        }

        private ActionResult AdminSpecialties(AdvisorDetailViewModel viewModel, bool isEditMode)
        {
            viewModel.LoadSecurity("advisors-index", viewModel);
            if (viewModel.CanView(SecurityConstants.Advisor.AdminTab))
            {
                return PartialView(isEditMode ? "EditorTemplates/Admin.TravelSpecialties" : "DisplayTemplates/Admin.TravelSpecialties", viewModel.Specialties);
            }
            return new HttpStatusCodeResult(HttpStatusCode.Forbidden);
        }
        [ValidateAntiForgeryToken]
        [AjaxPost]
        public ActionResult AdminSpecialtiesUpdate(AdvisorTravelSpecialtiesViewModel viewModel)
        {
            viewModel.LoadSecurity("advisors-adminspecialtiesupdate", viewModel);
            viewModel.AdvisorTravelSpecialties.LoadSecurity("advisors-adminspecialtiesupdate", viewModel.AdvisorTravelSpecialties);
            // TODO: wire the security object into this controller end-point, currently only in advisors-index, justin help
            // if (viewModel.CanView(SecurityConstants.Advisor.EditAdmin))
            {
                if (ModelState.IsValid)
                {
                    AdvisorInfoManager.SetInterestList(viewModel.AdvisorTravelSpecialties);
                    AdvisorInfoManager.SetDestinationList(viewModel.AdvisorTravelSpecialties);

                    var userMasterEntityId = FederatedLoginClient.User.UserMasterEntityId;
                    var success = AdvisorInfoManager.SaveTravelSpecialties(userMasterEntityId, viewModel.AdvisorTravelSpecialties);
                    return new HttpStatusCodeResult(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
                }
                else
                {
                    // Validation error(s) detected on the server side
                    var partialView = PartialView("EditorTemplates/Admin.TravelSpecialties", viewModel);
                    Response.StatusCode = (int)HttpStatusCode.PartialContent;
                    return partialView; 
                }
            }
        }
		[AjaxGet]
		public JsonResult HotelBookingData(int id)
		{
			var hotelBookingInfo = AdvisorViewManager.GetParentAgencyHotelBookingInfo(id);
			

			//if (hotelBookingInfo == null)
			//{
			//	// TODO: log it...
			//	return PageNotFoundActionResult();
			//}
			//else
			//{
				
			//	return PartialView("EditorTemplates/Admin.BookingProgram", hotelBookingInfo);
			//}
			return Json(hotelBookingInfo, JsonRequestBehavior.AllowGet);
		}

        [ValidateAntiForgeryToken]
        [AjaxPost]
        public ActionResult PersonalizationUpdate(DataAccess.Models.ProductDetail.Advisor.AdvisorPersonalizationInfo viewModel)
        {
            if (ModelState.IsValid)
            {
                if (viewModel.LogoUrl != null &&                                               // LogoUrl will be null if logo cannot be edited
                    viewModel.LogoUrl.Equals("REMOVE", StringComparison.OrdinalIgnoreCase))
                {
                    viewModel.LogoUrl = String.Empty;
                }

                var userMasterEntityId = FederatedLoginClient.User.UserMasterEntityId;
                var deleteCustomPersonalization = viewModel.UseAgencyDefaultPersonalization;
                var success = PersonalizationManager.PersonalizationSet(userMasterEntityId, !deleteCustomPersonalization, viewModel);

                // All we have to do is indicate success or failure.
                // For either, assume the page will respond in the proper manner.
                return new HttpStatusCodeResult(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            }
            else
            {
                // Validation error(s) detected on the server side
                var partialView = Personalization(viewModel.AssociatedMasterEntityId, true);     // round-trip the incoming model
                Response.StatusCode = (int)HttpStatusCode.PartialContent;     // same error as used in agency settings validation error
                return partialView;                                     // client will replace the existing editing partial with this one
            }
        }

        [AjaxGet]
        public ActionResult AdvisorSecurity(int id, bool isEditMode, bool mayDelete = false)
        {
            var vm = AgencySecurityViewManager.GetAdvisorSecurity(id);
            
         
            
            if (vm == null)
            {
                // TODO: log it...
                return PageNotFoundActionResult();
            }
            else
            {
           
                CacheManager.Add("advisorSecurityModel" + vm.Id, vm, new TimeSpan(0, 10, 0));
               
                vm.MayDeleteProfile = mayDelete && isEditMode;
                vm.CannotEditSecurityRoles = CannotEditSecurityRoles();
                vm.LoadSecurity("agencies-index", vm);
                var viewPath = isEditMode ? "EditorTemplates/Admin.AdvisorSecurity" : "DisplayTemplates/Admin.AdvisorSecurity";
                return PartialView(viewPath, vm);
            }
        }
        /// <summary>
        /// Update advisor security settings
        /// </summary>
        /// <param name="advisorSecurity"></param>
        /// <remarks>WARNING: the incoming data may contain an unencrypted password, and poses a security risk.
        /// The risk was discussed with Justin and Chintan, and they are aware of it.
        /// Since this same risk existed with Composer, we assume that the Business knowingly considers this is an acceptable risk.
        /// </remarks>
        /// <returns></returns>
        [ValidateAntiForgeryToken]
        [AjaxPost]
        public ActionResult AdvisorSecurityUpdate(AdvisorSecurityModel advisorSecurity)
        {
            //If you are not allowed to save this model, get out of here.
            if (!AuthorizedToSave("advisors-advisorsecurityupdate",advisorSecurity))
            {
                return null;
            }

           
           //var vm = (AdvisorSecurityModel)(HttpContext.Items["vm"]);
            // if LoginAlias hasn't changed, ignore validation errors
            if (advisorSecurity.LoginAlias == advisorSecurity.LoginAliasOriginal)
            {
                if (ModelState.ContainsKey("LoginAlias"))
                    ModelState["LoginAlias"].Errors.Clear();
            }

            if (advisorSecurity.Status == "Archive" || ModelState.IsValid)
            {
                var success = AgencySecurityViewManager.SetAdvisorSecurity(advisorSecurity);
                return new HttpStatusCodeResult(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            }
            else
            {
                var partialWithValidationErrors = PartialView("EditorTemplates/Admin.AdvisorSecurity", advisorSecurity);
                Response.StatusCode = (int)HttpStatusCode.PartialContent;

                return partialWithValidationErrors;
            }
        }


        // Of the roles which are involved in editing advisor security info,
        // single out the one role with lesser privilege (lowly agency staff person)
        private bool CannotEditSecurityRoles()
        {
            return FederatedLoginClient.User.IsMemberOf(SecurityRole.AgencyStaff) &&
                   !FederatedLoginClient.User.IsMemberOf(SecurityRole.AgencyLead) &&
                   !FederatedLoginClient.User.IsMemberOf(SecurityRole.AgencyLeadCompany);
        }

        //[AjaxPost]
        //public JsonResult SetAdvisorContactInfoView(AdvisorContactInfoViewModel contactInfo)
        //{
        //    AdvisorDetailManager advisorManager = new AdvisorDetailManager();
        //    ModelState.Clear();
        //    advisorManager.SetProfileInfo(contactInfo);
        //    return Json(new { type = "success", title = "Success!", message = "Your changes have been saved." });
        //}

        [AjaxGet]
        public JsonResult GetCountries()
        {
            var jsonResult = string.Empty;
            var allCountries = ProfileAddressManager.GetCountries();
            return Json(allCountries, JsonRequestBehavior.AllowGet);
        }

        public class ValidationStatus
        {
            public bool status { get; set; }
            public string message { get; set; }
        }
    
        public ActionResult LaunchInfoPage(int id)
        {
            var targetUrl = "/#/InfoPages?VM=IInfoPageViewModelParams&P_VM_MasterEntityTypeId=" + id;
            var urlWithAuthToken = ComposerSSOManager.GenerateRequestUri(targetUrl, FederatedLoginClient.User);
            return Redirect(urlWithAuthToken);
        }

        [AjaxPost]
        public ActionResult SaveSupplierMarketingOptions(SupplierMarketingOptionsViewModel vm)
        {
            // additional security check
            vm.LoadSecurity("advisors-index", vm);
            if (!vm.CanView("editButtonForSupplierMarketingOptions"))
                return PageNotFoundActionResult();

            var viewManager = new SupplierMarketingOptionsViewManager();
            viewManager.AdvisorSupplierMarketingOptionsSave(vm);

            ActionResult result = Json(new { Result = "OK" });
            Response.StatusCode = (int)HttpStatusCode.OK;

            return result;
        }

        [AjaxPost]
        public string GetAutoCompleteResults(AutoSuggestSearchParameters search)
        {
            var searchManager = new AutoCompleteSearchManager();
            var searchOptions = new SearchOptions
            {
                SearchMode = "Advisor",
                SearchType = SearchTypes.Advisor.ToString(),
                // SortType = "AdvisorNameAsc",
                FacetCategories = null,
                SearchTerms = search.SearchTerms + "*"
            };
            var results = searchManager.GetAllAdvisorsAutoCompleteList(searchOptions);
            return results;
        }

        [AjaxGet]
        public ContentResult GetEmail(int advisorMeid)
        {
            var searchManager = new SearchManager();
            var searchOptions = new SearchOptions
            {
                SearchMode = "Advisor",
                SearchType = SearchTypes.Advisor.ToString(),
                IncludeFacets = false, 
                SearchTerms = "meid:" + advisorMeid
            };

            var results = searchManager.GetSearchResults(searchOptions);
            if (results.Advisor.Results.IsNotNullOrEmpty())
            {
                return Content(results.Advisor.Results[0].Email);
            }

            return Content(string.Empty);
        }

        [AjaxGet]
        public JsonResult GetAutoCompleteResultsDetailed(string searchTerm)
        {
            const string defaultProfileImageWidthHeight = "150";
            var searchManager = new AutoCompleteSearchManager();
            var searchOptions = new SearchOptions
            {
                SearchMode = "Advisor",
                SearchType = SearchTypes.Advisor.ToString(),
                FacetCategories = null,
                SearchTerms = searchTerm
            };

            var results = searchManager.GetDetailedAdvisorAutoCompleteListEx(searchOptions);

            var suggestionList = new List<Suggestion>(results.docs.Count);
            foreach (var result in results.docs)
            {
                suggestionList.Add(new Suggestion()
                {
                    AdvisorCompany = (result.advisor_is_independent_contractor 
                        ? $"{SearchResultsHelpers.FormatAdvisorAffiliateOf(result.advisor_doing_business_as)} {result.advisor_company}" // customized company name
                        : result.advisor_company), 
                    AdvisorEmail = result.advisor_email,
                    AdvisorLocation = SearchResultsHelpers.FormatCityStateCountryFromJson(result.advisor_city_state_country, true),
                    AdvisorMeid = result.advisor_id,
                    AdvisorPhone = result.advisor_office_phone_number,
                    Label = result.name_suggestion,
                    ProfileImage = SearchResultsHelpers.GetImageUriFromWidthHeight(result.advisor_image_url, defaultProfileImageWidthHeight, defaultProfileImageWidthHeight),
                    OptedInHotelBooking = result.advisor_is_in_hotel_booking
                });
            }

            return Json(Framework.Utilities.Json.Serialize(suggestionList.ToArray()), 
                JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Fire and forget AJAX POST to log an "Advisor Contact" action
        /// </summary>
        /// <param name="data">Ajax payload to load</param>
        /// <returns>An empty response</returns>
        [AjaxPost]
        public ContentResult LogAdvisorContactAction(ContactAdvisorActionLogAjaxPost data)
        {
            // Try to parse and validate incoming data. If can't parse then mark as invalid
            // and keep going. Better to log partial data than throwing out the whole thing.
            ActionLogManager.ContactAdvisorAction action;
            if (!Enum.TryParse(data.Action, true, out action))
            {
                action = ActionLogManager.ContactAdvisorAction.Invalid;
            }

            ActionLogManager.ContactAdvisorPlatform platform;
            if (!Enum.TryParse(data.Platform, true, out platform))
            {
                platform = ActionLogManager.ContactAdvisorPlatform.Invalid;
            }

            ActionLogManager.ContactAdvisorSource source;
            if (!Enum.TryParse(data.Source, true, out source))
            {
                source = ActionLogManager.ContactAdvisorSource.Invalid;
            }

            // Fire & Forget
            System.Threading.Tasks.Task.Run(() => new ActionLogManager().LogContactAdvisorAction(data.AdvisorMeid, action, platform, source));

            // Return an empty response.
            return Content(string.Empty);
        }

        [HttpGet]
        public ActionResult AdminCommunities(int id)
        {
            var model = new DynamicsCommunitiesManager().GetUserCommunities(id);
            return PartialView("Partial/_Communities", model);
        }

        [AjaxPost]
        public JsonResult SaveConsumerAdvisorRelationship(int selectedAdvisorMeid, string hotelName, string roomDescription, DateTime? checkIn, DateTime? checkOut)
        {
            var advisorDetailViewManager = new Managers.AdvisorDetailManager();
            var result = advisorDetailViewManager.SaveConsumerAdvisorRelationship(selectedAdvisorMeid, hotelName, HttpUtility.HtmlDecode(roomDescription), checkIn, checkOut);
            return Json(result, JsonRequestBehavior.DenyGet);
        }

        [HttpGet]
        public ActionResult ConsumerNotifications(int advisorId, int agencyId, int agencyParentId, bool isEditMode)
        {
            var manager = new ConsumerNotificationsViewManager();
            var notifications = manager.GetNotificationsForAdvisor(advisorId, agencyId, agencyParentId);
            if (notifications == null)
            {
                var log = new Log("TouchPoints.Cms.Core.AdvisorsController");
                log.Error("AdvisorsController - ConsumerNotifications could not be found for id = " + advisorId);
                return PageNotFoundActionResult();
            }

            var viewModel = notifications;
            viewModel.LoadSecurity("advisors-index", viewModel);
            return PartialView(isEditMode ? "EditorTemplates/_ConsumerNotifications" : "DisplayTemplates/_ConsumerNotifications", viewModel);
        }

        [AjaxPost]
        public ActionResult ConsumerNotificationsUpdate(ConsumerNotificationsViewModel viewModel)
        {
            ValidationHelper.IgnoreValidationFor(this.ModelState, "NotificationEmail"); // not relevant in case of an advisor

            if (ModelState.IsValid)
            {
                var user = FederatedLoginClient.User;
                var success = (new ConsumerNotificationsViewManager()).UpdateNotifications(viewModel, user.UserMasterEntityId);
                return new HttpStatusCodeResult(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
            }
            else
            {
                Response.StatusCode = (int)HttpStatusCode.PartialContent;
                return PartialView("EditorTemplates/_ConsumerNotifications", viewModel);
            }
        }

	    private SearchResults SearchResults(int page = 0)
	    {
		    var searchManager = new SearchManager();
		    var results = page == 0
			    ? new SearchResults(searchManager.GetSeoSearchResults(SearchTypes.Advisor, DefaultSortType))
			    : new SearchResults(searchManager.GetSearchResultsByProductPage(page, SearchTypes.Advisor, DefaultSortType));

		    results.Breadcrumb = new BreadcrumbManager().GetBreadcrumbs(BreadcrumbTypes.Advisor);
		    return results;
	    }

	}
}