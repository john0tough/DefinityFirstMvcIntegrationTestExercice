using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using TouchPoints.Cms.Core.Enums;
using TouchPoints.Cms.Core.HelperClasses;
using TouchPoints.Cms.Core.HelperClasses.ReviewsAndRatings;
using TouchPoints.Cms.Core.Managers.AgencyProfile;
using TouchPoints.Cms.Core.Managers.SupplierMarketingOptions;
using TouchPoints.Cms.Core.Managers.ConsumerNotifications;
using TouchPoints.Cms.Core.Models;
using TouchPoints.Cms.Core.Models.Data.Advisor;
using TouchPoints.Cms.Core.Models.Lookup;
using TouchPoints.Cms.Core.Models.View;
using TouchPoints.Framework.UserAccount.SupportingClasses;
using TouchPoints.Framework.Utilities;
using TouchPoints.Cms.Core.DataAccess.Databases.Composer;
using TouchPoints.Cms.Core.DataAccess.HelperClass;
using TouchPoints.Cms.Core.DataAccess.Models.Composer;
using TouchPoints.Cms.Core.DataAccess.Models.ProductDetail.Agency;

//using TouchPoints.Cms.Core.DataAccess.Models.SOLR;
using TouchPoints.Services.HelperClasses.Product;
using TouchPoints.Services.Models.Advisor;
using TouchPoints.Services.ProductCatalog.Core.HelperClasses;
using TouchPoints.Services.DataAccess.Databases.Composer;
using TouchPoints.Services.DataAccess.Models.ProductDetail.Advisor;
using TouchPoints.Services.DataAccess.Models.ProductDetail.Common;
using TouchPoints.Services.DataAccess.Models.ProductDetail.Review;
using TouchPoints.Services.DataAccess.Models.SOLR;
using TouchPoints.Services.Managers;
using TouchPoints.Services.Managers.AdvisorProfile;
using TouchPoints.Services.Managers.Dynamics;
using TouchPoints.Services.ProductDetail.Caching;
using Virtuoso.SSO.Client.Authentication.Clients;
using Virtuoso.SSO.Client.TokenHandling;
using AdvisorCobrandAndSettingsManager = TouchPoints.Cms.Core.Services.Managers.AdvisorProfile.AdvisorCobrandAndSettingsManager;
using AdvisorContactInfo = TouchPoints.Cms.Core.DataAccess.Models.ProductDetail.Advisor.AdvisorContactInfo;
using AdvisorContactInfoManager = TouchPoints.Cms.Core.Services.Managers.AdvisorProfile.AdvisorContactInfoManager;
using AdvisorOutOfOffice = TouchPoints.Cms.Core.DataAccess.Models.ProductDetail.Advisor.AdvisorOutOfOffice;
using AdvisorPersonalizationInfo = TouchPoints.Cms.Core.DataAccess.Models.ProductDetail.Advisor.AdvisorPersonalizationInfo;

namespace TouchPoints.Cms.Core.Managers.AdvisorProfile
{
    public class AdvisorViewManager
    {
        public AdvisorDetailViewModel BuildAdvisorViewModel(int id)
        {
            AdvisorDetailViewModel viewModel = null;
            var manager = new Managers.AdvisorDetailManager();
            viewModel = manager.GetAdvisorDetailInfo(id, false);
            return viewModel;
        }

        public AdvisorDetailViewModel GetAdvisorDetailViewModel(int advisorId, bool isEdit)
        {
            var manager = new AdvisorManager();
            var detailInfo = manager.GetProductDetailInfo(advisorId);
            if (detailInfo == null)
                return null;

            detailInfo.IsEditable = AdvisorIsEditable(detailInfo.AdvisorMasterEntityId, detailInfo.AdvisorCompanyMasterEntityId, detailInfo.AdvisorCompanyParentMasterEntityId);
            detailInfo.IsPreview = false;
            detailInfo.AdvisorStatus = TypeChanger.DoEnum(detailInfo.Status, ProfileStatus.Unknown);
            detailInfo.Certifications = AdvisorInfoManager.GetAdvisorCertifications(detailInfo.AdvisorCertificationsXml);
            detailInfo.VisitedPlaces = AdvisorInfoManager.GetVisitedPlaces(detailInfo.AdvisorPlacesVisitedXml);
            detailInfo.Interests = BaseDetailInfoHelpers.GetTravelElements(detailInfo.AdvisorInterestTypesXml);
            detailInfo.SpecialtyDestinations = BaseDetailInfoHelpers.GetTravelElements(detailInfo.AdvisorSpecialtyCountriesXml);
            detailInfo.Languages = BaseDetailInfoHelpers.GetTravelElements(detailInfo.AdvisorLanguagesXml);
            detailInfo.AlliancePrograms = BaseDetailInfoHelpers.GetTravelElements(detailInfo.AdvisorAllianceProgramsXml);
            detailInfo.ConditionalLeadsName = AdvisorInfoManager.GetConditionalLeadsName(detailInfo.AlliancePrograms);
            detailInfo.AdvisorPrimarydAddress = ProfileContactInfoManagerBase.GetPrimaryAddress(detailInfo.ProfileAddressXml);
            ProfileAddressManager.DisambiguateAddressCity(detailInfo.AdvisorPrimarydAddress, isEdit);
            if (HttpContext.Current.Request.QueryString["consumer"] == "1")
            {
                detailInfo.IsEditable = false;
                detailInfo.IsPreview = true;
            }

            var user = FederatedLoginClient.User;
            var viewModel = new AdvisorDetailViewModel
            {
                AdvisorMasterEntityId = detailInfo.AdvisorMasterEntityId,
                Status = detailInfo.AdvisorStatus,
                AdvisorName = string.Format("{0} {1}", detailInfo.AdvisorFirstName, detailInfo.AdvisorLastName)
            };
            
            viewModel.ShowReviewsTab = ReviewHelper.ShowReview(detailInfo.TotalCompletedReviews, detailInfo.TotalBlockedReviews, detailInfo.TotalActiveReviews);
            if (viewModel.ShowReviewsTab)
                viewModel.ReviewsInfoJson = new RatingAndReviewManager().GetReviewsInfoJsonForAdvisor(detailInfo);

            viewModel.HeaderInfoPlus = new AdvisorHeaderInfoPlus
            {
                Header = new AdvisorHeaderInfoViewModel { AdvisorHeaderInfo = AdvisorInfoManager.BuildHeaderInfo(detailInfo)},
                ContactAdvisor = GetContactAdvisor(detailInfo),
                ReviewBadge = ReviewHelper.BuildBadgeModelForAdvisorDetailPage(detailInfo)
            };

            viewModel.AboutMePlus = new AdvisorAboutMePlus
            {
                Alliances = AdvisorInfoManager.BuildAboutMe(detailInfo),
                Overview = GetProfileOverviewInfo(detailInfo),
                IsActive = detailInfo.AdvisorIsActive,
                CompanyIsActive = detailInfo.AdvisorCompanyIsActive,
                AdvisorMasterEntityId = advisorId
            };

            if (user.IsComposerUser)
            {
                AddCommunityInfoForAdvisor(viewModel.AboutMePlus);
            }

            viewModel.DetailInfo = detailInfo;
            viewModel.ContactInfo = AdvisorContactInfoManager.BuildContactInfo(detailInfo);
            viewModel.CobrandInfo = new AdvisorCobrandInfoViewModel
            {
                AdvisorCobrandInfo = AdvisorCobrandAndSettingsManager.BuildCobrandInfo(detailInfo)
            };
            ProfileAddressManager.DisambiguateAddressCity(viewModel.ContactInfo.PrimaryAddress, isEdit);
            ProfileAddressManager.DisambiguateAddressCities(viewModel.ContactInfo.Addresses, isEdit);

            // note that if an advisor is using the agency default personalization, the advisor personalized address will be null
            var personalizedAddress = TouchPoints.Framework.Utilities.Xml.Deserialize<PersonalizationAddress>(detailInfo.AdvisorPersonalizedAddressXml) ?? new PersonalizationAddress();
            detailInfo.AdvisorPersonalizedAddress = personalizedAddress;
            viewModel.AdvisorPersonalization = new AdvisorPersonalizationInfo
            {
                AdvisorName = detailInfo.AdvisorPersonalizedName,
                AgencyName = detailInfo.AgencyPersonalizedName,
                DoingBusinessAs = detailInfo.AdvisorPersonalizedDoingBusinessAs,
                AddressLine1 = personalizedAddress.AddressLine1,
                AddressLine2 = personalizedAddress.AddressLine2,
                City = personalizedAddress.City,
                RegionNameEng = personalizedAddress.State,
                RegionId = personalizedAddress.RegionId,
                RegionCode = personalizedAddress.RegionCode,
                CountryNameEng = personalizedAddress.Country,
                CountryId = personalizedAddress.CountryId,
                PostalCode = personalizedAddress.PostalCode,
                Phone = detailInfo.AdvisorPersonalizedPrimaryPhone,
                SecondPhone = detailInfo.AdvisorPersonalizedSecondaryPhone,
                WebAddress = detailInfo.AdvisorPersonalizedWebsite,
                Email = detailInfo.AdvisorPersonalizedEmail,
                StateOfSellerId = detailInfo.AdvisorPersonalizedStateOfSellerId,
                LogoUrl = detailInfo.AdvisorCompanyLogo,
                AssociatedMasterEntityId = detailInfo.AdvisorMasterEntityId,
                AssociatedParentMasterEntityId = detailInfo.AdvisorCompanyParentMasterEntityId,
                UseAgencyDefaultPersonalization = detailInfo.UseAgencyDefaultPersonalization
            };

            personalizedAddress = Xml.Deserialize<PersonalizationAddress>(detailInfo.AgencyPersonalizedAddressXml) ?? new PersonalizationAddress();
            viewModel.AgencyPersonalization = new AgencyPersonalizationInfo
            {
                AgencyName = detailInfo.AgencyPersonalizedName,
                AddressLine1 = personalizedAddress.AddressLine1,
                AddressLine2 = personalizedAddress.AddressLine2,
                City = personalizedAddress.City,
                RegionNameEng = personalizedAddress.State,
                RegionId = personalizedAddress.RegionId,
                RegionCode = personalizedAddress.RegionCode,
                CountryNameEng = personalizedAddress.Country,
                CountryId = personalizedAddress.CountryId,
                PostalCode = personalizedAddress.PostalCode,
                Phone = detailInfo.AgencyPersonalizedPrimaryPhone,
                SecondPhone = detailInfo.AgencyPersonalizedSecondaryPhone,
                WebAddress = detailInfo.AgencyPersonalizedWebsite,
                Email = detailInfo.AgencyPersonalizedEmail,
                StateOfSellerId = detailInfo.AgencyPersonalizedStateOfSellerId,
                LogoUrl = detailInfo.LogoUrl,
                AssociatedMasterEntityId = detailInfo.AdvisorCompanyMasterEntityId,
                AssociatedParentMasterEntityId = detailInfo.AdvisorCompanyParentMasterEntityId,
            };
            viewModel.Information = new AdvisorInformationViewModel
            {
                AdvisorInformation = AdvisorInfoManager.BuildAdminInfo(detailInfo)
            };
            viewModel.Specialties = new AdvisorTravelSpecialtiesViewModel
            {
                AdvisorTravelSpecialties = AdvisorInfoManager.BuildTravelSpecialties(detailInfo)
            };

            // fill in personalization IC status, since the value in DetailInfo may be unreliable
            viewModel.AdvisorPersonalization.IsIndependentContractor = (viewModel.Information.AdvisorType.TypeId == AdvisorInfoManager.IndependentContractorTypeId);

            // TODO: only need these for edit by certain people with permissions
            if (null != viewModel.Specialties)
            {
                viewModel.Specialties.SpecialtyCountriesLookup = SpecialtyDestinations.Get(null, ProfileType.Advisor);
            }

            viewModel.OutOfOffice = new AdvisorOutOfOffice();
            viewModel.Settings = AdvisorCobrandAndSettingsManager.BuildAdvisorSettings(detailInfo);
            AgencyViewManager.CalculateFieldLevelPermissions(viewModel.Settings, user);

            viewModel.SupplierMarketingOptions =
                new SupplierMarketingOptionsViewManager().GetSupplierMarketingOptionsForMember(advisorId);

            viewModel.SupplierMarketingOptions.MemberHasOverrideRole = detailInfo.OverrideAgencySupplierMarketing;
            viewModel.SupplierMarketingOptions.AgencyMeid = detailInfo.AdvisorCompanyMasterEntityId;

            viewModel.ConsumerNotifications = new ConsumerNotificationsViewManager().GetNotificationsForAdvisor(advisorId, viewModel.Settings.Meid, viewModel.Settings.ParentMeid);

            viewModel.SecurityData = new AdvisorProfileSecurityData();

            var securityModel = TouchPoints.Framework.Utilities.Xml.Deserialize<StaffSecurityModelXml>(detailInfo.ProfileRolesXml);
            viewModel.AdvisorSecurity = SecurityManager.BuildAdvisorSecurity(securityModel);
            viewModel.HotelBooking = new AdvisorHotelBookingViewModel();

            viewModel.HotelBooking.HotelBooking = new AdvisorHotelBooking
	        {
		        AdvisorMasterEntityId = viewModel.AdvisorMasterEntityId,
				AgentId = detailInfo.AgentId,
		        BookingPrefatoryCode = detailInfo.BookingPrefatoryCode,
		        BookingPseudoCityCode = detailInfo.BookingPseudoCityCode,
		        BookingQueueNumber = detailInfo.BookingQueueNumber,
		        AgentInterfaceId = detailInfo.AgentInterfaceId,
		        SameAsAgency = detailInfo.IsBookingSameAsAgency
	        };

            viewModel.AdvisorLogoExists = !String.IsNullOrEmpty(viewModel.AdvisorPersonalization.LogoUrl);
            viewModel.AgencyDefaultLogoExists = !String.IsNullOrEmpty(viewModel.AgencyPersonalization.LogoUrl);
            viewModel.PersonalizationGuidelinesUrl = Config.AppSettings.PersonalizationGuidelinesUrl;

            return viewModel;
        }

	    public static bool SetHotelBookingInfo(AdvisorHotelBooking hotelBooking)
	    {
		    return AdvisorInfoManager.SaveHotelBooking(FederatedLoginClient.User.UserMasterEntityId, hotelBooking);
	    }


        public static bool ValidateProfileAddresses(AdvisorContactInfo info, ModelStateDictionary modelState)
        {
            TpMapOptions mapOptions;
            var maps = new Dictionary<string, string>();
            var passed = true;
            var address = info.PrimaryAddress;
            var validateNamePrefix = "PrimaryAddress";
            var isValid = ProfileAddressManager.ValidateProfileAddress(address, modelState, validateNamePrefix,
                out mapOptions);
            passed &= isValid;
            if (mapOptions != null)
            {
                maps.Add("", Json.Serialize(mapOptions));
            }

            if (info.Addresses.IsNotNullOrEmpty())
            {
                for (var i = 0; i < info.Addresses.Count; i++)
                {
                    validateNamePrefix = string.Format("Addresses[{0}]", i);
                    isValid = ProfileAddressManager.ValidateProfileAddress(info.Addresses[i], modelState, validateNamePrefix,
                        out mapOptions);
                    passed &= isValid;
                    if (mapOptions != null)
                    {
                        maps.Add(info.Addresses[i].City, Json.Serialize(mapOptions));
                    }
                }
            }

            if (maps.Count > 0)
            {
                info.MapsJson = maps;
            }

            return passed;
        }

        public static bool ValidateAdvisorIds(AdvisorInformationViewModel model, ModelStateDictionary modelState)
        {
            if (model.ListAdvisorIds.IsNullOrEmpty())
                return true;

            for (var i = model.ListAdvisorIds.Count - 1; i >= 0; i--)
            {
                if (model.ListAdvisorIds[i].Id.Contains(","))
                {
                    var ids = model.ListAdvisorIds[i].Id.Split(',');
                    foreach(var id in ids)
                    {
                        var advisorId = new AdvisorIdClass();
                        advisorId.Id = id.Trim();
                        advisorId.Status = ProfileItemStatus.Insert;
                        model.ListAdvisorIds.Add(advisorId);

                    }
                    model.ListAdvisorIds.Remove(model.ListAdvisorIds[i]);
                }

            }
            for(int i = 0; i < model.ListAdvisorIds.Count; i++)
            {
                var validateName = string.Format("ListAdvisorIds[{0}].Id", i);
                var validateStatus = string.Format("ListAdvisorIds[{0}].Status", i);
                if (!modelState.ContainsKey(validateName))
                {
                    var state = new ModelState();
                    //state.Value = new ValueProviderResult();
                    modelState.Add(validateName, state);
                    modelState.Add(validateStatus, state);
                }
            }

            var keys = modelState.Keys.Where(k => k.StartsWith("ListAdvisorIds")).ToList();
            keys.ForEach(k => modelState[k].Value = null);

            var availableIds = model.ListAdvisorIds.Where(i => i.Status != ProfileItemStatus.Delete && i.Id.IsNotNullOrEmpty()).ToList();
	        if (availableIds.Count == 0)
		        return true;
            var last = availableIds.Last();
            var exists = false;
            if (availableIds.Count > 1)
            {
                var others = availableIds.Take(availableIds.Count - 1);
                exists = others.Any(i => i.Id.Equals(last.Id, StringComparison.OrdinalIgnoreCase));
            }

            if (exists)
                {
                for (var i = model.ListAdvisorIds.Count - 1; i >= 0; i--)
                    {
                    if (model.ListAdvisorIds[i].Id.IsNotNullOrEmpty() && model.ListAdvisorIds[i].Id.Equals(last.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                var validateName = string.Format("ListAdvisorIds[{0}].Id", i);
                        modelState[validateName].Errors.Add("ID numbers must be unique within your agency");
                                return false;
                            }
                }
            }

            var isValid = true;
            if (!exists)
            {
                for (var i = model.ListAdvisorIds.Count - 1; i >= 0; i--)
                {
                    var id = model.ListAdvisorIds[i].Id;
                    var advisorId = availableIds.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (advisorId != null)
                    {
                        exists = Advisor.AdvisorIdExists(model.AdvisorParentCompanyMasterEntityId, model.AdvisorMasterEntityId, id);
                        if (exists)
                        {
                            isValid = false;
                            var validateName = string.Format("ListAdvisorIds[{0}].Id", i);
                            modelState[validateName].Errors.Add("ID numbers must be unique within your agency");
                        }
                    }
                }
            }

            return isValid;
        }

        private ProfileOverviewInfo GetProfileOverviewInfo(AdvisorDetailInfo info)
        {
            var mapOptions = GetTpMapOptions(info);
            var mapOptionsJson = Json.Serialize(mapOptions);
            var model = new ProfileOverviewInfo(info.IsEditable, info.AdvisorAboutMe, info.VisitedPlaces, mapOptionsJson,
                info.AdvisorCompanyParentMasterEntityId, info.AdvisorCompanyMasterEntityId, info.AdvisorMasterEntityId);
            model.IsPreview = info.IsPreview;

            return model;
        }

        private ContactEntityModel GetContactAdvisor(AdvisorDetailInfo info)
        {
            var isConditionalLead = !string.IsNullOrEmpty(HttpContext.Current.Request.QueryString["cla"]);
            var selectedCla = new List<string>();
            if (isConditionalLead)
            {
                var token = HttpContext.Current.Request.QueryString["cla"];
                var iv = Config.AppSettings.AesIv;
                var key = Config.AppSettings.AesKey;
                string conditionalLeadsName = new TokenManager(iv, key).DeTokenize(token).ConditionalLeadsName;
                if (!string.IsNullOrEmpty(conditionalLeadsName))
                {
                    var conditionalLeadsList = conditionalLeadsName.Split(true, new[] { '|' });
                    selectedCla = conditionalLeadsList.Intersect(info.ConditionalLeadsName).ToList();
                    isConditionalLead = selectedCla.Any();
                }
                else
                {
                    isConditionalLead = false;
                }
            }
            
            var contactAdvisor = AdvisorContact.GetContactModel(info, FederatedLoginClient.User, isConditionalLead);
            if (isConditionalLead)
                contactAdvisor.ConditionalLeadsName = selectedCla;
            contactAdvisor.ContactUri = ContactEntityHelper.BuildContactUri(contactAdvisor, true);
            return contactAdvisor;
        }

        private TpMapOptions GetTpMapOptions(AdvisorDetailInfo info)
        {
            var options = new TpMapOptions();
            options.ShowNumbers = false;
            options.ShowPushpins = true;
            options.ShowMapList = true;
            options.ClosePreviousInfoWindow = true;
            options.ZoomLevel = "auto";
            options.MapType = TpMapOptions.MapTypeTerrain;

            foreach (var poi in info.VisitedPlaces)
            {
                var seq = TypeChanger.Do(poi.PointOfInterestId, 0);
                string title = null;

                if (string.Equals(poi.CountryName, "United States", StringComparison.CurrentCultureIgnoreCase) || string.Equals(poi.CountryName, "Canada", StringComparison.CurrentCultureIgnoreCase))

                    title = string.Format("{0}, {1}", poi.PlaceName, poi.CountryName);
                else
                {
                    var place = poi.PlaceName;
                    if (place.IndexOf('(') > -1)
                    {
                        place = place.Substring(0, poi.PlaceName.IndexOf('('));
                    }
                    title = string.Format("{0}, {1}", place, poi.CountryName);
                }

                options.MapMarkers.Add(new TpMapOptions.TpMapMarker()
                {
                    Id = seq,
                    SequenceLabel = poi.PointOfInterestId.ToString(),
                    Latitude = poi.Latitude.ToString("0.0"),
                    Longitude = poi.Longitude.ToString("0.0"),
                    Title = title,
                    InfoWindowHtml = "<h3>" + poi.PlaceName + ", " + poi.CountryName + "</h3><div>Last Trip: " + poi.LastYearVisited + "<br/>Number of Trips: " + poi.NumOfVisits,
                    CountryCodeIso2 = poi.CountryCodeISO2,
                    NumOfVisits = poi.NumOfVisits.ToString(),
                    Date = new DateTime(poi.LastYearVisited, 1, 1).ToString("yyyy")
                });
            }
            return options;
        }

        public static bool AdvisorIsEditable(int meId, int companyMeId, int parentCompanyMeId)
        {
            if (Config.AppSettings.SiteInReadOnlyMode)
            {
                return false;
            }
            var user = FederatedLoginClient.User;
            bool retVal = false;
            if (user.IsLoggedIn)
            {
                // Advisor may edit their own
                var isMyAdvisorProfile = user.IsAdvisor && (meId == user.UserMasterEntityId);

                // Agency Lead 
                var isAgencyLead = user.SecurityRoles.Contains(SecurityRole.AgencyLead)
                    && (companyMeId == user.OrgMasterEntityId);

                // Agency Company Lead
                var isAgencyCompanyLead =
                    user.SecurityRoles.Contains(SecurityRole.AgencyLeadCompany)
                    && (parentCompanyMeId == user.ParentOrgMasterEntityId);

                // Virtuoso (Composer) System Admin and Power User may edit the advisor's profile
                var isComposerSysad = user.IsComposerSysad;
                var isVirtuosoPowerUser = user.IsMemberOf(SecurityRole.VirtuosoPowerUser);

                retVal = isMyAdvisorProfile || isAgencyLead || isAgencyCompanyLead || isComposerSysad || isVirtuosoPowerUser;

            }
            return retVal;
        }

	    public static AdvisorHotelBooking GetParentAgencyHotelBookingInfo(int advisorId)
	    {
		    var hotelBookingInfo = AdvisorInfoManager.GetHotelBookingInfoForParentAgency(advisorId);
		    if (hotelBookingInfo != null)
			    return new AdvisorHotelBooking
			    {
				 
				    BookingPrefatoryCode = hotelBookingInfo.BookingPrefatoryCode,
				    BookingPseudoCityCode = hotelBookingInfo.BookingPseudoCityCode,
				    BookingQueueNumber = hotelBookingInfo.BookingQueueNumber,
				    AdvisorMasterEntityId = advisorId,
				    SameAsAgency = true


			    };

		    return null;

	    }

        public static ReviewsInfo GetRatingAndReviewInfo(AdvisorDetailInfo detailInfo)
        {
            RatingAndReviewManager ratingAndReviewManager = new RatingAndReviewManager();
            ReviewsInfo reviewseviewsInfo = ratingAndReviewManager.GetReviewsInfoForAdvisor(detailInfo);
            ratingAndReviewManager.GetReviews(reviewseviewsInfo);
            ReviewsPaging reviewaPaging = new ReviewsPaging();
            reviewaPaging.SortType = ReviewsPaging.SortTypeNone;
            ratingAndReviewManager.SortReviews(reviewseviewsInfo, reviewaPaging);

            return reviewseviewsInfo;
        }

        protected void AddCommunityInfoForAdvisor(AdvisorAboutMePlus model)
        {
            model.AdvisorCommunities = new DynamicsCommunitiesManager().GetUserCommunities(model.AdvisorMasterEntityId);
            // filter to growth only
            model.AdvisorCommunities.CommunitiesList =
                  model.AdvisorCommunities.CommunitiesList.Where(
                      x => x.CommunityType.Equals("GROWTH", StringComparison.InvariantCultureIgnoreCase)).ToList();
        }
    }
}