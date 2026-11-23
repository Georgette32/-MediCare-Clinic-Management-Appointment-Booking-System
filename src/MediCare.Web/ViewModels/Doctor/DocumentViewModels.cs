using MediCare.Application.DTOs.Documents;

namespace MediCare.Web.ViewModels.Doctor;

public sealed class DoctorPatientDocumentsViewModel
{
    public Guid PatientProfileId { get; set; }

    public string PatientName { get; set; } = string.Empty;

    public IReadOnlyList<MedicalDocumentDto> Documents { get; set; } = Array.Empty<MedicalDocumentDto>();
}
