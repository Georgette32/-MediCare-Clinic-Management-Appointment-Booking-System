using MediCare.Domain.Entities;

namespace MediCare.Infrastructure.DemoData;

/// <summary>A weekly working window: the days it applies to and its clinic-local start and end, in minutes after midnight.</summary>
internal sealed record DemoWindow(DayOfWeek[] Days, int StartMinute, int EndMinute);

/// <summary>
/// One demo doctor. The last three values shape the queue history: consultation lengths are lognormal with a mean of
/// <c>AppointmentDurationMinutes × ServiceMeanFactor</c> and the coefficient of variation <see cref="ServiceCv"/>;
/// walk-ins per working day are Poisson with mean <see cref="WalkInsPerDay"/>.
/// </summary>
internal sealed record DemoDoctorSpec(
    string Key,
    string Email,
    string FullName,
    string Specialization,
    int AppointmentDurationMinutes,
    string Biography,
    DemoWindow[] Windows,
    bool Active = true,
    double ServiceMeanFactor = 1.0,
    double ServiceCv = 0.45,
    double WalkInsPerDay = 2.0);

/// <summary>One realistic visit: what the patient said, what the doctor found, and the medicines written.</summary>
internal sealed record DemoClinicalCase(
    string ChiefComplaint,
    string Diagnosis,
    string ClinicalNotes,
    PrescriptionItemDraft[] Medications,
    string? PrescriptionNotes = null);

/// <summary>Fixed content for the demo data set: staff, doctors, patient names and clinical cases per specialization.</summary>
internal static class DemoDataCatalog
{
    private static readonly DayOfWeek[] SatToThu =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];
    private static readonly DayOfWeek[] SunToThu =
        [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

    private static int H(int hour, int minute = 0) => hour * 60 + minute;

    public static readonly DemoDoctorSpec[] Doctors =
    [
        new("youssef", "dr.youssef@medicare.test", "Dr Youssef El-Masry", "Cardiology", 20,
            "Consultant cardiologist with 15 years of experience in hypertension, heart failure and preventive cardiology. Fellow of the European Society of Cardiology.",
            [
                new(SunToThu, H(10), H(14)),
                new([DayOfWeek.Tuesday, DayOfWeek.Thursday], H(17), H(21))
            ],
            // Cardiology: long, careful consultations and a steady stream of walk-in complaints.
            ServiceMeanFactor: 1.08, ServiceCv: 0.45, WalkInsPerDay: 1.75),
        new("mariam", "dr.mariam@medicare.test", "Dr Mariam Hassan", "Pediatrics", 15,
            "Pediatrician focused on newborn care, growth follow-up, childhood vaccinations and common childhood infections.",
            [new(SatToThu, H(16), H(22))],
            // Pediatrics: consultation lengths swing widely with the child; many parents come in without booking.
            ServiceMeanFactor: 1.0, ServiceCv: 0.52, WalkInsPerDay: 2.0),
        new("khaled", "dr.khaled@medicare.test", "Dr Khaled Nabil", "Orthopedics", 30,
            "Orthopedic surgeon specialised in sports injuries, knee and shoulder problems, and back pain.",
            [
                new([DayOfWeek.Sunday, DayOfWeek.Tuesday, DayOfWeek.Thursday], H(12), H(18)),
                new([DayOfWeek.Saturday], H(10), H(14))
            ],
            // Orthopedics: examinations and injury reviews vary a lot; walk-ins are mostly sports injuries.
            ServiceMeanFactor: 1.1, ServiceCv: 0.5, WalkInsPerDay: 1.5),
        new("nadia", "dr.nadia@medicare.test", "Dr Nadia Farouk", "Dermatology", 20,
            "Dermatologist treating acne, eczema, psoriasis, hair loss and skin allergies for adults and children.",
            [new([DayOfWeek.Saturday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday], H(15), H(21))],
            // Dermatology: short, routine skin checks; a busy walk-in rash clinic.
            ServiceMeanFactor: 0.95, ServiceCv: 0.4, WalkInsPerDay: 2.5),
        new("amr", "dr.amr@medicare.test", "Dr Amr Saleh", "Internal Medicine", 20,
            "Internist caring for adults with diabetes, high blood pressure, stomach and chest complaints. Morning clinic six days a week.",
            [
                new(SatToThu, H(9), H(13)),
                new([DayOfWeek.Saturday, DayOfWeek.Tuesday, DayOfWeek.Wednesday], H(18), H(22))
            ],
            // General internal medicine: the busiest walk-in desk, with the widest spread of consultation lengths.
            ServiceMeanFactor: 1.02, ServiceCv: 0.55, WalkInsPerDay: 3.5),
        new("laila", "dr.laila@medicare.test", "Dr Laila Ibrahim", "Ophthalmology", 20,
            "Ophthalmologist offering eye examinations, glasses prescriptions, dry-eye care and diabetic eye screening.",
            [new([DayOfWeek.Sunday, DayOfWeek.Tuesday, DayOfWeek.Thursday], H(11), H(17))],
            // Ophthalmology: refraction and screening checks are fairly uniform; a few urgent eye complaints walk in.
            ServiceMeanFactor: 0.92, ServiceCv: 0.38, WalkInsPerDay: 2.0),
        new("hany", "dr.hany@medicare.test", "Dr Hany Zaki", "Dentistry", 30,
            "General dentist. No longer practising at the clinic.",
            [new([DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Wednesday], H(13), H(19))],
            // Dentistry: procedures run long and unevenly; toothache walk-ins.
            Active: false, ServiceMeanFactor: 1.12, ServiceCv: 0.5, WalkInsPerDay: 2.0)
    ];

    public static readonly string[] FirstNames =
    [
        "Salma", "Omar", "Mona", "Ahmed", "Yasmin", "Mostafa", "Hoda", "Tarek", "Rania", "Mahmoud",
        "Dina", "Sherif", "Nourhan", "Karim", "Aya", "Islam", "Reem", "Hossam", "Marwa", "Ali",
        "Noha", "Bassem", "Fatma", "Waleed", "Sara", "Mohamed", "Eman", "Ramy", "Heba", "Ziad"
    ];

    public static readonly string[] LastNames =
    [
        "Adel", "Khalil", "Fathy", "Mansour", "Gamal", "Ragab", "Sherif", "Lotfy", "Saber", "Fouad",
        "Helmy", "Wahba", "Essam", "Fahmy", "Mahmoud", "Hamdy", "Tawfik", "Badr", "Kamal", "Ezzat",
        "Samy", "Rizk", "Abdelaziz", "Anwar", "Magdy", "Ashraf", "Younis", "Galal", "Selim", "Morsy"
    ];

    private static PrescriptionItemDraft Med(string name, string dosage, string frequency, int days, string? instructions = null) =>
        new(name, dosage, frequency, days, instructions);

    public static readonly IReadOnlyDictionary<string, DemoClinicalCase[]> Cases = new Dictionary<string, DemoClinicalCase[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Cardiology"] =
        [
            new("Chest tightness on exertion for two weeks", "Stable angina pectoris",
                "BP 145/90, HR 82. ECG: no acute ST changes. Exercise stress test requested.",
                [
                    Med("Aspirin", "75 mg", "Once daily after lunch", 30),
                    Med("Bisoprolol", "5 mg", "Once daily in the morning", 30),
                    Med("Nitroglycerin", "0.4 mg sublingual", "As needed for chest pain", 30, "Maximum 3 doses, 5 minutes apart. Go to the emergency department if the pain persists.")
                ], "Avoid heavy exertion until the stress test is done."),
            new("Palpitations and occasional dizziness", "Paroxysmal supraventricular tachycardia",
                "Regular rhythm at rest. 24-hour Holter monitor requested. Vagal manoeuvres explained.",
                [Med("Propranolol", "10 mg", "Three times daily", 14)]),
            new("Follow-up of high blood pressure", "Essential hypertension, controlled",
                "BP 132/84. Continue the current plan and a low-salt diet.",
                [Med("Amlodipine", "5 mg", "Once daily", 30)]),
            new("Shortness of breath and ankle swelling", "Heart failure with preserved ejection fraction (NYHA II)",
                "Bilateral ankle oedema. Echocardiography requested. Daily weight monitoring advised.",
                [
                    Med("Furosemide", "40 mg", "Once daily in the morning", 14),
                    Med("Spironolactone", "25 mg", "Once daily", 30)
                ], "Weigh yourself every morning and call the clinic if you gain more than 2 kg in 3 days."),
            new("Routine check after starting a statin", "Dyslipidemia",
                "LDL improved from 172 to 118 mg/dL. Recheck the lipid profile in 3 months.",
                [Med("Atorvastatin", "20 mg", "Once daily at bedtime", 60)])
        ],
        ["Pediatrics"] =
        [
            new("Fever and sore throat for two days", "Acute streptococcal pharyngitis",
                "Temperature 38.6 C, tonsillar exudate. Rapid strep test positive.",
                [
                    Med("Amoxicillin suspension 250 mg/5 ml", "7.5 ml", "Twice daily", 10, "Complete the full course even if the child feels better."),
                    Med("Paracetamol syrup 120 mg/5 ml", "10 ml", "Every 6 hours as needed for fever", 5)
                ]),
            new("Runny nose and cough", "Common cold (viral upper respiratory infection)",
                "Chest clear, good hydration. Reassurance, fluids and rest.",
                [Med("Saline nasal drops", "2 drops in each nostril", "Four times daily", 7)]),
            new("Diarrhoea and vomiting since yesterday", "Acute gastroenteritis with mild dehydration",
                "Oral rehydration plan explained. Return if the child cannot keep fluids down.",
                [
                    Med("Oral rehydration salts", "1 sachet in 200 ml water", "After each loose stool", 3),
                    Med("Zinc syrup", "20 mg", "Once daily", 10)
                ]),
            new("Itchy rash on cheeks and elbows", "Atopic dermatitis",
                "Dry eczematous patches. Moisturise twice daily and avoid hot baths.",
                [Med("Hydrocortisone 1% cream", "Thin layer", "Twice daily", 7)]),
            new("Routine growth and vaccination visit", "Healthy child, routine check",
                "Weight and height on the 50th percentile. Vaccinations up to date.",
                []),
            new("Ear pain and fever", "Acute otitis media, right ear",
                "Bulging right tympanic membrane. Temperature 38.2 C.",
                [
                    Med("Amoxicillin suspension 250 mg/5 ml", "10 ml", "Twice daily", 7),
                    Med("Ibuprofen syrup 100 mg/5 ml", "5 ml", "Every 8 hours after food", 3)
                ])
        ],
        ["Orthopedics"] =
        [
            new("Lower back pain after lifting a heavy box", "Acute mechanical low back pain",
                "No neurological deficit, straight-leg raise negative. Physiotherapy referral given.",
                [
                    Med("Ibuprofen", "400 mg", "Three times daily after meals", 7),
                    Med("Methocarbamol", "750 mg", "Twice daily", 5)
                ]),
            new("Right knee pain when climbing stairs", "Primary osteoarthritis of the right knee",
                "X-ray: mild medial joint space narrowing. Weight loss and quadriceps exercises advised.",
                [
                    Med("Diclofenac 1% gel", "Apply to the knee", "Three times daily", 14),
                    Med("Glucosamine", "1500 mg", "Once daily", 30)
                ]),
            new("Wrist pain after a fall", "Sprain of the left wrist, no fracture on X-ray",
                "Wrist splint for two weeks. Review if pain persists.",
                [Med("Paracetamol", "1 g", "Every 8 hours as needed", 5)]),
            new("Shoulder pain when raising the arm", "Rotator cuff tendinopathy, right shoulder",
                "Painful arc 70-120 degrees. Home exercise programme demonstrated.",
                [Med("Naproxen", "500 mg", "Twice daily after food", 10)]),
            new("Heel pain with the first steps in the morning", "Plantar fasciitis",
                "Stretching programme and supportive footwear advised.",
                [])
        ],
        ["Dermatology"] =
        [
            new("Acne on the face for several months", "Acne vulgaris, moderate",
                "Comedones and inflammatory papules on cheeks and forehead. Daily sunscreen advised.",
                [
                    Med("Adapalene 0.1% gel", "Pea-sized amount", "Once daily at night", 60, "Apply to the whole face, not only to the spots."),
                    Med("Doxycycline", "100 mg", "Once daily after food", 30)
                ]),
            new("Itchy scaly patches on the elbows", "Plaque psoriasis",
                "Well-defined plaques on both elbows, about 3% body surface area.",
                [Med("Clobetasol 0.05% ointment", "Thin layer", "Twice daily", 14)]),
            new("Red itchy rash after using a new detergent", "Allergic contact dermatitis",
                "Eczematous rash on the forearms. Avoid the suspected detergent.",
                [
                    Med("Cetirizine", "10 mg", "Once at night", 10),
                    Med("Mometasone 0.1% cream", "Thin layer", "Once daily", 7)
                ]),
            new("Hair shedding for three months", "Telogen effluvium",
                "Positive pull test, no scarring. CBC, ferritin and TSH requested.",
                [Med("Biotin", "5 mg", "Once daily", 90)]),
            new("Itchy ring-shaped rash on the thigh", "Tinea corporis",
                "Annular scaly plaque with central clearing.",
                [Med("Terbinafine 1% cream", "Thin layer", "Twice daily", 14)])
        ],
        ["Internal Medicine"] =
        [
            new("Tiredness and frequent urination", "Type 2 diabetes mellitus, newly diagnosed",
                "HbA1c 7.8%. Diet and exercise counselling given. Review in one month.",
                [Med("Metformin", "500 mg", "Twice daily with meals", 30)]),
            new("Burning pain in the upper stomach after meals", "Gastro-oesophageal reflux disease",
                "Epigastric tenderness, no alarm features. Lifestyle advice given.",
                [Med("Omeprazole", "20 mg", "Once daily before breakfast", 28)]),
            new("Fever, cough and body aches", "Influenza-like illness",
                "Temperature 38.4 C, chest clear. Rest and fluids.",
                [Med("Paracetamol", "500 mg", "Every 6 hours as needed", 5)]),
            new("Burning on urination", "Uncomplicated urinary tract infection",
                "Urine dipstick positive for nitrites and leukocytes.",
                [Med("Nitrofurantoin", "100 mg", "Twice daily", 5)]),
            new("Follow-up of diabetes and blood pressure", "Type 2 diabetes with hypertension, stable",
                "Fasting glucose 128 mg/dL, BP 128/80. Continue treatment.",
                [
                    Med("Metformin", "1000 mg", "Twice daily with meals", 30),
                    Med("Lisinopril", "10 mg", "Once daily", 30)
                ]),
            new("Headache and dizziness", "Iron deficiency anaemia",
                "Hb 10.2 g/dL, low ferritin. Dietary advice given.",
                [Med("Ferrous sulfate", "325 mg", "Once daily", 60, "Take with orange juice. It may darken the stool.")])
        ],
        ["Ophthalmology"] =
        [
            new("Red itchy eyes with discharge", "Bacterial conjunctivitis",
                "Bilateral injection with mucopurulent discharge. Visual acuity normal.",
                [Med("Moxifloxacin 0.5% eye drops", "1 drop in each eye", "Three times daily", 7)]),
            new("Blurred distance vision", "Myopia, refraction updated",
                "New glasses prescription: right -2.25, left -2.00.",
                []),
            new("Dry, gritty eyes at work", "Dry eye syndrome",
                "Reduced tear break-up time. Screen breaks every 20 minutes advised.",
                [Med("Artificial tears", "1 drop in each eye", "Four times daily", 30)]),
            new("Seasonal itchy eyes", "Allergic conjunctivitis",
                "Papillary reaction, clear discharge.",
                [Med("Olopatadine 0.1% eye drops", "1 drop in each eye", "Twice daily", 14)]),
            new("Routine diabetic eye check", "No diabetic retinopathy",
                "Dilated fundus examination normal. Repeat in 12 months.",
                [])
        ],
        ["Dentistry"] =
        [
            new("Toothache in the lower left jaw", "Irreversible pulpitis, tooth 36",
                "Root canal treatment started.",
                [
                    Med("Amoxicillin", "500 mg", "Three times daily", 5),
                    Med("Ibuprofen", "400 mg", "Every 8 hours after food", 3)
                ]),
            new("Bleeding gums when brushing", "Gingivitis",
                "Plaque deposits, bleeding on probing. Oral hygiene instructions given.",
                [Med("Chlorhexidine 0.12% mouthwash", "15 ml", "Twice daily", 14)]),
            new("Routine check and cleaning", "Scaling and polishing done",
                "No caries detected. Next check in six months.",
                [])
        ]
    };

    public static readonly string[] Addenda =
    [
        "Lab results reviewed: values within the expected range. No change to the plan.",
        "Patient called the clinic: symptoms improved after three days of treatment.",
        "X-ray report received and reviewed. Findings agree with the clinical diagnosis.",
        "Follow-up phone call: medication tolerated well, no side effects reported."
    ];

    public static readonly string[] WalkInRejectionNotes =
    [
        "Fully booked today. Please book an appointment for tomorrow.",
        "Not urgent. Please book a regular appointment."
    ];
}
