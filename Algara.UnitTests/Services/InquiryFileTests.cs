using System.Text;
using Algara.Web.Services;
using Algara.Web.Services.Inquiries;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
namespace Algara.UnitTests.Services;
public sealed class InquiryFileTests
{
    private static InquiryFiles Service() => new(new ProductImageStorage(new Mock<IWebHostEnvironment>().Object));
    private static IFormFile File(byte[] bytes, string name) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Files", name);
    public static byte[] Pdf()
    {
        var text = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>" };
        for (var i=0;i<objects.Length;i++) { offsets.Add(text.Length); text.Append($"{i+1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = text.Length; text.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
    [Fact] public async Task ValidPdfUsesSafeNameAndPreservesDocument()
    {
        var bytes=Pdf(); var rows=await Service().PrepareAsync(new[]{File(bytes,"../../private.PDF")},default);
        Assert.Equal("sketch-1.pdf",rows.Single().FileName);Assert.Equal(bytes,rows.Single().Contents);
    }
    [Fact] public async Task HtmlRenamedPdfRejected() => await Assert.ThrowsAsync<ProductImageValidationException>(() =>
        Service().PrepareAsync(new[]{File(Encoding.UTF8.GetBytes("<html>fake document</html>"),"sketch.pdf")},default));
    [Fact] public async Task BatchAboveLimitRejected() => await Assert.ThrowsAsync<ProductImageValidationException>(() =>
        Service().PrepareAsync(Enumerable.Range(0,6).Select(_=>File(Pdf(),"sketch.pdf")).ToArray(),default));
    [Fact] public async Task OversizeRejected() => await Assert.ThrowsAsync<ProductImageValidationException>(() =>
        Service().PrepareAsync(new[]{File(new byte[InquiryFiles.MaxBytes+1],"sketch.pdf")},default));
    [Fact] public async Task EmptyRejected() => await Assert.ThrowsAsync<ProductImageValidationException>(() =>
        Service().PrepareAsync(new[]{File([],"sketch.pdf")},default));
    [Fact] public async Task ActualStreamSizeEnforcedEvenWhenReportedSizeLies()
    {
        var upload=new Mock<IFormFile>();upload.SetupGet(x=>x.Length).Returns(100);upload.SetupGet(x=>x.FileName).Returns("sketch.pdf");
        upload.Setup(x=>x.OpenReadStream()).Returns(new MemoryStream(new byte[InquiryFiles.MaxBytes+1]));
        await Assert.ThrowsAsync<ProductImageValidationException>(()=>Service().PrepareAsync(new[]{upload.Object},default));
    }
}
