using System;
using System.Web;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using TouchPoints.Cms.Core.Controllers;

namespace IntegrationTest
{
   [TestClass]
   public class IntegrationTestAdvisorController
   {
      private int otherConsumerId;
      private int redirectConsumerId;
      private int validConsumerId;
      private int emptyViewModelConsumerId;
      private string expectedUrl;

      /// <summary>
      /// Usually [TestInitialize] is used to configure initial services and values 
      /// and this configured services and values are available for all test methods 
      /// </summary>
      [TestInitialize]
      public void Setup()
      {
         this.otherConsumerId = 999;
         this.redirectConsumerId = 998;
         this.validConsumerId = 997;
         this.emptyViewModelConsumerId = 996;
         this.expectedUrl = "http://myfakeactionurl.com";
      }

      /// <summary>
      /// This test verify when no Id is passed to index action
      /// </summary>
      [TestMethod]
      public void TestIndexToPageNotFound()
      {
         var controller = new AdvisorsController();
         var response = controller.Index();
         Assert.IsInstanceOfType(response, typeof(PageNotFoundActionResult));
      }

      /// <summary>
      /// This method test when SecurityRules.IsConsumerViewingOtherAdvisorProfile(id) return true
      /// and Request.Url is not null.
      /// In this test the assumption is made that the redirect() method internally returns a RedirectToRouteResult
      /// </summary>
      [TestMethod]
      public void TestIndexConsumerViewingOtherAdvisorProfile()
      {
         var controller = new AdvisorsController();
         var expectedRedirect = string.Format("{0}=query=values", AdvisorHelper.GetMyAdvisorUri);

         // set HttpContext to define Request url
         controller.ControllerContext = new ControllerContext()
         {
            Controller = controller,
            HttpContext = this.GetFakeIndexHttpContext()
         };

         var response = controller.Index(this.otherConsumerId);

         Assert.AreEqual(expectedRedirect, (response as RedirectToRouteResult).RouteValues["action"]);
         Assert.IsInstanceOfType(response, typeof(RedirectToRouteResult));
      }

      /// <summary>
      /// This method test when service SecurityRules.IsConsumerViewingOtherAdvisorProfile(id) return true
      /// and Request.Url is null
      /// </summary>
      [TestMethod]
      public void TestIndexConsumerViewingOtherAdvisorProfileEmptyUrl()
      {
         var controller = new AdvisorsController();
         var response = controller.Index(this.otherConsumerId);
         Assert.IsInstanceOfType(response, typeof(PageForbiddenActionResult));
      }

      /// <summary>
      /// This method test when service SeoHelper.ValidateSlug(id, Request.RawUrl);
      /// returns a valid url
      /// </summary>
      [TestMethod]
      public void TestIndexValidatedSlug()
      {
         var expectedUrl = SeoHelper.ValidateSlug(this.redirectConsumerId, this.expectedUrl);
         var controller = new AdvisorsController();
         var response = controller.Index(this.redirectConsumerId);

         Assert.AreEqual(expectedUrl, (response as RedirectResult).Url);
         Assert.IsInstanceOfType(response, typeof(RedirectResult));
      }

      /// <summary>
      /// This method test when service manager.BuildAdvisorViewModel(id);
      /// returns a null AdvisorDetailViewModel
      /// </summary>
      [TestMethod]
      public void TestIndexEmptyViewModel()
      {

         var controller = new AdvisorsController();
         var response = controller.Index(this.emptyViewModelConsumerId);

         Assert.IsInstanceOfType(response, typeof(PageNotFoundActionResult));
      }

      /// <summary>
      /// This Test evaluate index happy path
      /// </summary>
      [TestMethod]
      public void TestIndexHappyPath()
      {
         var expectedViewModel = new AdvisorDetailViewModel()
         {
            attr1 = "value1",
            attr2 = "value2"
         };

         var controller = new AdvisorsController();
         var response = controller.Index(this.validConsumerId);


         // In order to maintain this simple test,
         // an evaluation of each attribute is carried out,
         // but when it is necessary to evaluate complex objects it is advisable to serialize both objects,
         // the expected value and the current value and the Equal Assertion is made with both serialized values
         Assert.AreEqual(expectedViewModel.attr1, (response as ViewResult).Model.attr1);
         Assert.AreEqual(expectedViewModel.attr2, (response as ViewResult).Model.attr2);
         Assert.IsInstanceOfType(response, typeof(ViewResult));
      }

      /// <summary>
      ///  Usually [TestCleanup] method is used when is needed to revert changes derived from test methods
      /// </summary>
      [TestCleanup]
      public void Cleanup()
      {
         // no revert actions needed
      }

      /// <summary>
      ///  Configure a Fake HttpContext
      /// </summary>
      /// <returns></returns>
      private HttpContextBase GetFakeIndexHttpContext()
      {
         //Arrange
         var requestUrl = new Uri("http://myrequesturl");
         var request = Mock.Of<HttpRequestBase>();
         var requestMock = Mock.Get(request);
         requestMock.Setup(m => m.Url).Returns(requestUrl);

         var httpcontext = Mock.Of<HttpContextBase>();
         var httpcontextSetup = Mock.Get(httpcontext);
         httpcontextSetup.Setup(m => m.Request).Returns(request);


         var actionName = "Index";
         var expectedUrl = this.expectedUrl;
         var mockUrlHelper = new Mock<UrlHelper>();
         mockUrlHelper
            .Setup(m => m.Action(actionName, "Advisors", It.IsAny<object>(), It.IsAny<string>()))
            .Returns(expectedUrl)
            .Verifiable();

         return httpcontext;
      }
   }
}