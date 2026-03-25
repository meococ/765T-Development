using System;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class JsonUtilTests
{
    [Fact]
    public void DeserializeRequired_Throws_On_Empty_Json()
    {
        Assert.ThrowsAny<Exception>(() => JsonUtil.DeserializeRequired<AddTextNoteRequest>(string.Empty));
    }

    [Fact]
    public void DeserializePayloadOrDefault_Returns_Default_Instance_On_Empty_Json()
    {
        var payload = JsonUtil.DeserializePayloadOrDefault<AddTextNoteRequest>(string.Empty);
        Assert.NotNull(payload);
        Assert.Equal(string.Empty, payload.Text);
        Assert.True(payload.UseViewCenterWhenPossible);
    }

    [Fact]
    public void TryDeserialize_Returns_False_On_Invalid_Json()
    {
        var ok = JsonUtil.TryDeserialize<AddTextNoteRequest>("{ not-json }", out var payload, out var error);
        Assert.False(ok);
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void DeserializeRequired_Parses_Valid_Json()
    {
        var payload = JsonUtil.DeserializeRequired<AddTextNoteRequest>("{\"Text\":\"xin chao\",\"UseViewCenterWhenPossible\":true}");
        Assert.Equal("xin chao", payload.Text);
        Assert.True(payload.UseViewCenterWhenPossible);
    }
}
