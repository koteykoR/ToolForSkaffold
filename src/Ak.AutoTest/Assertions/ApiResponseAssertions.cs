using Ak.AutoTest.Clients;
using Ak.AutoTest.Reporting;
using FluentAssertions;
using System.Net;

namespace Ak.AutoTest.Assertions;

public static class ApiResponseAssertions
{
    public static void ShouldHaveStatusCode(this ApiResponse response, HttpStatusCode expected) =>
        AllureReport.Step($"HTTP-код ответа {expected}", () =>
        {
            AllureReport.AttachJson("response", response.Body);
            response.StatusCode.Should().Be(expected, "тело ответа: {0}", response.Body);
        });

    public static T AssertStatusAndRead<T>(this ApiResponse response, HttpStatusCode expected)
    {
        response.ShouldHaveStatusCode(expected);
        return response.Deserialize<T>();
    }

    public static void ShouldHaveHeader(this ApiResponse response, string name, string expected)
    {
        response.Headers.Should().ContainKey(name);
        response.Headers[name].Should().Contain(expected);
    }

    public static void ShouldHaveEmptyBody(this ApiResponse response) => response.Body.Should().BeEmpty();
}
