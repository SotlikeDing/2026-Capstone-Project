using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests;

public class UpdateProfileValidatorTests
{
    [Theory]
    [InlineData("Johan", "van Wyk", "082 555 0101", "8001015009087")]
    [InlineData("Anna-Marie", "O'Neill", "+27825550101", "A12345678")]
    [InlineData("Zoë", "du Plessis", "071550020", "")]
    [InlineData("Karoo", "Optics", "0215550107", "DEMO-KAROO.OPTICS-INVALID")] // the demo cast's placeholder IDs must stay saveable
    public void Accepts_valid_profiles(string name, string surname, string phone, string id)
    {
        var request = new UpdateProfileRequest { Name = name, Surname = surname, Phone = phone, IdPassportNum = id };
        Assert.Null(UpdateProfileValidator.Validate(request));
    }

    [Theory]
    [InlineData("", "Nel", "0825550101")]
    [InlineData("Pieter1", "Nel", "0825550101")]
    [InlineData("<script>", "Nel", "0825550101")]
    [InlineData("Pieter", "Nel", "082 555")]                // too few digits
    [InlineData("Pieter", "Nel", "+27 (0) 82 555 0101")]    // longer than the 15-character column
    [InlineData("Pieter", "Nel", "082-CALL-ME")]
    public void Rejects_bad_names_and_phones(string name, string surname, string phone)
    {
        var request = new UpdateProfileRequest { Name = name, Surname = surname, Phone = phone };
        Assert.NotNull(UpdateProfileValidator.Validate(request));
    }

    [Fact]
    public void Trims_values_in_place()
    {
        var request = new UpdateProfileRequest { Name = "  Johan ", Phone = " 0825550101 " };
        Assert.Null(UpdateProfileValidator.Validate(request));
        Assert.Equal("Johan", request.Name);
        Assert.Equal("0825550101", request.Phone);
    }

    [Fact]
    public void Null_fields_are_not_checked()
    {
        // A picture-only update leaves the other fields null; it must not fail on them.
        Assert.Null(UpdateProfileValidator.Validate(new UpdateProfileRequest { ProfilePictureUrl = "" }));
    }

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgo=", true)]
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://example.com/me.png", false)]
    public void Only_image_data_urls_are_accepted_as_pictures(string picture, bool ok)
    {
        var error = UpdateProfileValidator.Validate(new UpdateProfileRequest { ProfilePictureUrl = picture });
        Assert.Equal(ok, error == null);
    }

    [Fact]
    public void Rejects_oversized_picture()
    {
        var picture = "data:image/png;base64," + new string('A', UpdateProfileValidator.MaxPictureLength);
        Assert.NotNull(UpdateProfileValidator.Validate(new UpdateProfileRequest { ProfilePictureUrl = picture }));
    }
}
