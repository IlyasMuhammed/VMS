using VMS.Modules.BusinessPartners.Services;
using Xunit;

namespace VMS.Tests.BusinessPartners;

/// <summary>S1-BP-07: the format rules of FSD §6 and §8, and the name comparison behind the duplicate warning (§12.1).</summary>
public sealed class PartnerFormatTests
{
    [Theory]
    [InlineData("35202-1234567-1", true)]
    [InlineData("3520212345671", false)]      // the dashes are part of the format
    [InlineData("35202-123456-1", false)]
    [InlineData("35202-1234567-12", false)]
    [InlineData("3520a-1234567-1", false)]
    [InlineData("", false)]
    public void A_cnic_is_five_seven_and_one_digits(string value, bool valid) => Assert.Equal(valid, PartnerFormats.IsCnic(value));

    [Theory]
    [InlineData("1234567", true)]
    [InlineData("1234567-8", true)]
    [InlineData("1234567890123", true)]       // an individual's NTN is the 13-digit CNIC number
    [InlineData("35202-1234567-1", true)]
    [InlineData("123456", false)]
    [InlineData("ABCDEFG", false)]
    public void An_ntn_is_read_leniently_so_a_valid_one_is_never_refused(string value, bool valid) => Assert.Equal(valid, PartnerFormats.IsNtn(value));

    [Theory]
    [InlineData("0300-1234567", "0300-1234567")]
    [InlineData("03001234567", "0300-1234567")]           // a missing dash is added
    [InlineData("0300 1234567", "0300-1234567")]          // spaces are ignored
    [InlineData("+923001234567", "+923001234567")]        // international numbers are kept as entered
    [InlineData("0400-1234567", null)]
    [InlineData("0300-123456", null)]
    [InlineData("hello", null)]
    [InlineData("", null)]
    public void A_mobile_is_stored_in_one_form(string value, string? stored) => Assert.Equal(stored, PartnerFormats.NormalizeMobile(value));

    [Theory]
    [InlineData("0300-1234567", true)]
    [InlineData("042-35761234", true)]        // a landline is fine as a second number
    [InlineData("12", false)]
    [InlineData("abc-defgh", false)]
    public void An_alternate_phone_checks_only_characters_and_length(string value, bool valid) => Assert.Equal(valid, PartnerFormats.IsPhone(value));

    [Theory]
    [InlineData("accounts@ali-traders.pk", true)]
    [InlineData("accounts@localhost", false)]
    [InlineData("Ali <a@b.pk>", false)]
    [InlineData("not an email", false)]
    public void An_email_is_a_plain_address(string value, bool valid) => Assert.Equal(valid, PartnerFormats.IsEmail(value));

    [Theory]
    [InlineData("0123456789", true)]
    [InlineData("0123-4567-89", true)]
    [InlineData("01234567890123456789012345678901234", false)]   // 35 characters: one too many
    [InlineData("12AB", false)]
    public void An_account_number_is_digits_and_dashes_up_to_34(string value, bool valid) => Assert.Equal(valid, PartnerFormats.IsAccountNumber(value));

    [Theory]
    [InlineData("PK36SCBL0000001123456702", true)]
    [InlineData("PK37SCBL0000001123456702", false)]      // one wrong check digit
    [InlineData("PK36SCBL000000112345670", false)]       // one digit short
    [InlineData("GB29NWBK60161331926819", false)]        // not a Pakistani IBAN
    public void An_iban_needs_its_checksum(string value, bool valid) => Assert.Equal(valid, PartnerFormats.IsIban(value));

    // ── Names ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Ali Traders (Pvt.) Ltd", "ali traders")]
    [InlineData("  ALI   TRADERS ", "ali traders")]
    [InlineData("Ali & Sons Co.", "ali sons")]
    public void A_name_is_reduced_to_the_words_that_tell_it_apart(string name, string expected) => Assert.Equal(expected, NameSimilarity.Normalize(name));

    [Fact]
    public void The_same_name_dressed_differently_is_the_same_name()
    {
        Assert.Equal(1.0, NameSimilarity.Score("Ali Traders (Pvt.) Ltd", "ALI TRADERS"));
        Assert.Equal(1.0, NameSimilarity.Score("Traders Ali", "Ali Traders"));   // word order does not matter
    }

    [Fact]
    public void A_typo_is_close_and_a_different_name_is_not()
    {
        Assert.True(NameSimilarity.Score("Muhammad Ali Traders", "Muhamad Ali Traders") >= 0.85);
        Assert.True(NameSimilarity.Score("Ali Traders", "Bilal Motors") < 0.5);
        Assert.Equal(0, NameSimilarity.Score("", "Ali Traders"));
        Assert.Equal(0, NameSimilarity.Score("Pvt Ltd", "Ali Traders"));          // nothing left once the legal form is removed
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("same", "same", 0)]
    public void Distance_counts_single_character_edits(string a, string b, int expected) => Assert.Equal(expected, NameSimilarity.Distance(a, b));
}
