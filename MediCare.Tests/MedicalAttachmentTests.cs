using System.Security.Cryptography;
using System.Text;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Storage;

namespace MediCare.Tests;

/// <summary>Sample file contents with real magic numbers, so the tests exercise byte checks rather than names.</summary>
internal static class FileBytes
{
    public static byte[] Pdf(int length = 64) => Build(length, "%PDF-1.7\n"u8.ToArray());

    public static byte[] Jpeg(int length = 64) => Build(length, [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);

    public static byte[] Png(int length = 64) => Build(length, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

    /// <summary>A Windows executable: starts with "MZ", so it must be refused whatever its name says.</summary>
    public static byte[] Executable(int length = 64) => Build(length, [0x4D, 0x5A, 0x90, 0x00]);

    private static byte[] Build(int length, byte[] header)
    {
        var bytes = new byte[Math.Max(length, header.Length)];
        Array.Fill(bytes, (byte)'x');
        header.CopyTo(bytes, 0);
        return bytes;
    }

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

public sealed class MedicalAttachmentDomainTests
{
    [Fact]
    public void Detect_recognises_pdf_jpeg_and_png_by_their_leading_bytes_and_nothing_else()
    {
        Assert.Equal(MedicalAttachment.ContentTypePdf, MedicalFileSignature.Detect(FileBytes.Pdf()));
        Assert.Equal(MedicalAttachment.ContentTypeJpeg, MedicalFileSignature.Detect(FileBytes.Jpeg()));
        Assert.Equal(MedicalAttachment.ContentTypePng, MedicalFileSignature.Detect(FileBytes.Png()));
        Assert.Null(MedicalFileSignature.Detect(FileBytes.Executable()));
        Assert.Null(MedicalFileSignature.Detect("%PD"u8));
        Assert.Null(MedicalFileSignature.Detect(new byte[] { 0xFF, 0xD8 }));
    }

    [Theory]
    [InlineData("..\\..\\lab.pdf", "lab.pdf")]
    [InlineData("C:\\Users\\Nour\\scan.jpg", "scan.jpg")]
    [InlineData("folder/sub/report.png", "report.png")]
    [InlineData("bad\u0001name\u007f.pdf", "badname.pdf")]
    [InlineData("..", "attachment")]
    [InlineData("   ", "attachment")]
    [InlineData(null, "attachment")]
    public void Sanitizing_keeps_only_the_last_segment_and_strips_control_characters(string? raw, string expected) =>
        Assert.Equal(expected, MedicalAttachment.SanitizeOriginalFileName(raw));

    [Fact]
    public void Original_file_names_are_capped_at_the_column_length()
    {
        var sanitized = MedicalAttachment.SanitizeOriginalFileName(new string('a', 400) + ".pdf");

        Assert.Equal(MedicalAttachment.MaxOriginalFileNameLength, sanitized.Length);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.pdf", true)]
    [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.pdf", false)]
    [InlineData("../0123456789abcdef0123456789abcdef.pdf", false)]
    [InlineData("0123456789abcdef0123456789abcdef.exe", false)]
    [InlineData("lab.pdf", false)]
    [InlineData(null, false)]
    public void Stored_file_names_must_match_the_generated_shape(string? name, bool valid) =>
        Assert.Equal(valid, MedicalAttachment.IsValidStoredFileName(name));

    [Fact]
    public void Generated_stored_names_are_valid_and_carry_the_type_extension()
    {
        var name = MedicalAttachment.CreateStoredFileName(MedicalAttachment.ContentTypeJpeg);

        Assert.True(MedicalAttachment.IsValidStoredFileName(name));
        Assert.EndsWith(".jpg", name);
        Assert.Equal(MedicalAttachment.StoredFileNameLength, name.Length);
    }

    [Fact]
    public void Create_refuses_unsupported_types_empty_files_bad_digests_and_mismatched_extensions()
    {
        var recordId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var name = MedicalAttachment.CreateStoredFileName(MedicalAttachment.ContentTypePdf);
        var digest = FileBytes.Sha256Hex(FileBytes.Pdf());
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => MedicalAttachment.Create(recordId, doctorId, "x.exe", "application/x-msdownload", 10, name, digest, null, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => MedicalAttachment.Create(recordId, doctorId, "x.pdf", MedicalAttachment.ContentTypePdf, 0, name, digest, null, now));
        Assert.Throws<ArgumentException>(() => MedicalAttachment.Create(recordId, doctorId, "x.pdf", MedicalAttachment.ContentTypePdf, 10, name, "ABC", null, now));
        Assert.Throws<ArgumentException>(() => MedicalAttachment.Create(recordId, doctorId, "x.pdf", MedicalAttachment.ContentTypeJpeg, 10, name, digest, null, now));
        Assert.Throws<ArgumentException>(() => MedicalAttachment.Create(recordId, doctorId, "x.pdf", MedicalAttachment.ContentTypePdf, 10, "../evil.pdf", digest, null, now));
        Assert.Throws<ArgumentException>(() => MedicalAttachment.Create(recordId, doctorId, "x.pdf", MedicalAttachment.ContentTypePdf, 10, name, digest, new string('d', 501), now));
    }

    [Fact]
    public void Create_stores_the_sanitized_name_and_trimmed_description()
    {
        var name = MedicalAttachment.CreateStoredFileName(MedicalAttachment.ContentTypePdf);

        var attachment = MedicalAttachment.Create(Guid.NewGuid(), Guid.NewGuid(), "C:\\temp\\lab result.pdf", MedicalAttachment.ContentTypePdf,
            64, name, FileBytes.Sha256Hex(FileBytes.Pdf()), "  Blood panel  ", DateTimeOffset.UtcNow);

        Assert.Equal("lab result.pdf", attachment.OriginalFileName);
        Assert.Equal("Blood panel", attachment.Description);
        Assert.Equal(name, attachment.StoredFileName);
    }

    [Fact]
    public void Adding_an_attachment_to_another_record_is_refused()
    {
        var doctorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var entry = QueueEntry.CreateScheduled(Guid.NewGuid(), doctorId, Guid.NewGuid(), new DateOnly(2026, 12, 1), now);
        entry.Call(now);
        entry.StartConsultation(now);
        var record = MedicalRecord.CreateForVisit(entry, doctorId, null, "Flu", null, now);
        var attachment = MedicalAttachment.Create(Guid.NewGuid(), Guid.NewGuid(), "x.pdf", MedicalAttachment.ContentTypePdf, 64,
            MedicalAttachment.CreateStoredFileName(MedicalAttachment.ContentTypePdf), FileBytes.Sha256Hex(FileBytes.Pdf()), null, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => record.AddAttachment(attachment));
    }
}

public sealed class MedicalFileValidatorTests
{
    private const long Limit = 1024;

    [Fact]
    public void Real_pdf_jpeg_and_png_content_is_accepted_with_the_detected_type()
    {
        Assert.Equal(MedicalAttachment.ContentTypePdf, MedicalFileValidator.Validate(FileBytes.Pdf(), Limit).ContentType);
        Assert.Equal(MedicalAttachment.ContentTypeJpeg, MedicalFileValidator.Validate(FileBytes.Jpeg(), Limit).ContentType);
        Assert.Equal(MedicalAttachment.ContentTypePng, MedicalFileValidator.Validate(FileBytes.Png(), Limit).ContentType);
    }

    [Fact]
    public void An_executable_renamed_to_pdf_is_refused_because_its_bytes_are_not_a_pdf()
    {
        var result = MedicalFileValidator.Validate(FileBytes.Executable(), Limit);

        Assert.False(result.IsValid);
        Assert.Contains("content matches", result.Error);
    }

    [Fact]
    public void Empty_content_is_refused()
    {
        var result = MedicalFileValidator.Validate(ReadOnlySpan<byte>.Empty, Limit);

        Assert.False(result.IsValid);
        Assert.Equal("The file is empty.", result.Error);
    }

    [Fact]
    public void Content_larger_than_the_limit_is_refused_even_when_its_type_is_valid()
    {
        var result = MedicalFileValidator.Validate(FileBytes.Pdf((int)Limit + 1), Limit);

        Assert.False(result.IsValid);
        Assert.Contains("larger than", result.Error);
    }

    [Fact]
    public void Content_exactly_at_the_limit_is_accepted()
    {
        Assert.True(MedicalFileValidator.Validate(FileBytes.Pdf((int)Limit), Limit).IsValid);
    }
}

public sealed class MedicalFileStorageLocationTests
{
    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "medicare-content");
    private static readonly string WebRoot = Path.Combine(ContentRoot, "wwwroot");

    [Fact]
    public void A_relative_path_resolves_against_the_content_root_outside_wwwroot()
    {
        var resolved = MedicalFileStorageLocation.Resolve("App_Data/medical-files", ContentRoot, WebRoot);

        Assert.Equal(Path.GetFullPath(Path.Combine(ContentRoot, "App_Data", "medical-files")), resolved);
    }

    [Theory]
    [InlineData("wwwroot/medical")]
    [InlineData("wwwroot")]
    public void A_location_inside_the_web_root_is_refused_at_startup(string configured)
    {
        var path = Path.IsPathRooted(configured) ? configured : Path.Combine(ContentRoot, configured);

        Assert.Throws<InvalidOperationException>(() => MedicalFileStorageLocation.Resolve(path, ContentRoot, WebRoot));
    }

    [Fact]
    public void A_traversal_out_of_the_web_root_into_it_is_still_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MedicalFileStorageLocation.Resolve("../medicare-content/wwwroot/files", ContentRoot, WebRoot));
    }

    [Fact]
    public void A_missing_path_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => MedicalFileStorageLocation.Resolve("  ", ContentRoot, WebRoot));
    }
}

public sealed class FileSystemMedicalFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "medicare-store-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Save_then_open_round_trips_the_bytes_and_reports_size_and_sha256()
    {
        var store = new FileSystemMedicalFileStore(_root);
        var name = MedicalAttachment.CreateStoredFileName(MedicalAttachment.ContentTypePdf);
        var bytes = FileBytes.Pdf(100_000);

        var saved = await store.SaveAsync(new MemoryStream(bytes), name, CancellationToken.None);
        await using var stream = await store.OpenReadAsync(name, CancellationToken.None);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        Assert.Equal(bytes.Length, saved.SizeBytes);
        Assert.Equal(FileBytes.Sha256Hex(bytes), saved.Sha256Hex);
        Assert.Equal(bytes, copy.ToArray());
        Assert.True(Directory.Exists(_root), "The storage folder is created on construction.");
        Assert.True(File.Exists(Path.Combine(_root, name)));
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("..\\escape.pdf")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("lab report.pdf")]
    [InlineData("0123456789abcdef0123456789abcdef.exe")]
    public async Task Crafted_or_malformed_stored_names_are_rejected_before_any_path_is_built(string name)
    {
        var store = new FileSystemMedicalFileStore(_root);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new MemoryStream(FileBytes.Pdf()), name, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync(name, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(name, CancellationToken.None));
    }

    [Fact]
    public async Task A_stored_name_is_never_overwritten()
    {
        var store = new FileSystemMedicalFileStore(_root);
        var name = MedicalAttachment.CreateStoredFileName(MedicalAttachment.ContentTypePdf);
        await store.SaveAsync(new MemoryStream(FileBytes.Pdf()), name, CancellationToken.None);

        await Assert.ThrowsAnyAsync<IOException>(() => store.SaveAsync(new MemoryStream(FileBytes.Pdf(80)), name, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_the_file_and_deleting_twice_is_harmless()
    {
        var store = new FileSystemMedicalFileStore(_root);
        var name = MedicalAttachment.CreateStoredFileName(MedicalAttachment.ContentTypePng);
        await store.SaveAsync(new MemoryStream(FileBytes.Png()), name, CancellationToken.None);

        await store.DeleteAsync(name, CancellationToken.None);
        await store.DeleteAsync(name, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_root, name)));
    }

    [Fact]
    public async Task Opening_a_missing_file_reports_file_not_found()
    {
        var store = new FileSystemMedicalFileStore(_root);
        var name = MedicalAttachment.CreateStoredFileName(MedicalAttachment.ContentTypeJpeg);

        await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync(name, CancellationToken.None));
    }

    [Fact]
    public void A_relative_root_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new FileSystemMedicalFileStore("App_Data/medical-files"));
    }
}
