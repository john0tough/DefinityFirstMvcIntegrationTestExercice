# DefinityFirstMvcIntegrationTestExercice
Integration Test exercice for Definity First.

This project contains an integration test for controller given for this test `AdvisorController.cs`.
The method choosed for test was Index mehod.

----
The exercise is described bellow:

 1. A new solution was created with name `IntegrationTestExercise`  and  a new mvc project was added with same name. In this project was added the provided file for test `AdvisorController.cs`.
 2. Also a new MSTest project was added to solution, with name `Integration Test`.
 3. The dependencies Test project were: **Microsoft.ASPNET.MVC** to have access to MVC core objects, **Moq** to make Mocks for HttpContext, also a reference to **System.Web** in test project to test redirections and HttpContext.
4. Test class was called `IntegrationTestAdvisorController` this class was decored with `[TestClass]`. Also were added methods with test configuration atributes  as `[TestInitialize]` to Setup All dependencies needed for all tests inside this test class, and `[Cleanup]` is used to revert all undesired changes produced for tests, this is because integration tests could make use of production services.
 5. They were made six test methods with [TestMethod] atribute to evaluate all six posible paths detected inside Index method. 
 This paths were:
	 - When index receive no consumer id.
	 - When SecurityRules.IsConsumerViewingOtherAdvisorProfile(id) return true
and Request.Url is not null.
	 - When service SecurityRules.IsConsumerViewingOtherAdvisorProfile(id) return true and Request.Url is null 
	 - When service SeoHelper.ValidateSlug(id, Request.RawUrl) returns a valid url
	 - When service manager.BuildAdvisorViewModel(id) returns a null AdvisorDetailViewModel
	 - When index method finish his happy path.
