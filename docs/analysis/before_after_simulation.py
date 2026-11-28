"""
Before/after clinic queue SIMULATION for the MediCare graduation project.

IMPORTANT: every number this script prints or writes is SIMULATED. There is no
real clinic data behind it. The two queue rules are run on the same simulated
patients, so the comparison shows what each rule would do under the
assumptions below. It is not a measurement of the real clinic.

Assumptions (plain English)

 1. Doctors: the six ACTIVE doctors in MediCare.Infrastructure/DemoData/
    DemoDataCatalog.cs. Dr Hany Zaki is inactive and is excluded.
 2. One session per doctor per simulated day: the doctor's longest single
    weekly window. Where two windows tie, the first one listed in the catalog
    is used. Real weeks have several windows; this is a simplification.
 3. Booked slots: slots of AppointmentDurationMinutes start at the window start
    and run back to back. Only slots that finish inside the window are used.
    Each slot is booked with probability 0.72.
 4. No-shows: each booked patient fails to show with probability 0.10. No-shows
    are removed in BOTH scenarios.
 5. Consultation length: lognormal, mean = slot x ServiceMeanFactor, coefficient
    of variation = ServiceCv (sigma^2 = ln(1 + CV^2), mu = ln(mean) - sigma^2/2),
    clamped to [4, 55] minutes. The clamp shifts the mean slightly. Booked
    patients and walk-ins use the same distribution.
 6. Walk-ins: count ~ Poisson(WalkInsPerDay). Arrival = window start +
    (window length - 30 min) x max(U1, U2), so walk-ins cluster late in the
    session.
 7. Common random numbers: every random draw for a day (who books, no-shows,
    consultation lengths, walk-in counts and times, and the standard-normal
    noise behind each arrival rule) is drawn once and reused by every scenario.
    The scenarios therefore differ only in queue rules and arrival behaviour.
 8. AFTER (system, timed slots): a booked patient arrives at slot time + offset.
    offset ~ Normal(-8, 9) min, plus with probability 0.12 an extra U(10, 30) min.
    The offset is clamped to [-45, 3 x slot - 5] min.
 9. BEFORE (first-come queue, everyone told to come when the clinic opens):
    arrival = window start + Normal(20, 35) min, clamped to [-45, +180] min.
    Sensitivity runs use Normal(10, 20) ("BEFORE narrow") and Normal(30, 50)
    ("BEFORE wide"), with the same clamp. They reuse the same noise draws.
10. AFTER dispatch: whenever the doctor is free, call the waiting booked patient
    with the earliest slot. If none is waiting, call the earliest-arrived
    waiting walk-in. If nobody is waiting, the doctor waits for the next arrival.
11. BEFORE dispatch: strict first-come, first-served by arrival time, booked
    patients and walk-ins mixed.
12. The doctor starts at the window start (never earlier), serves without
    interruption, and keeps going until everyone has been seen.
13. Wait = start - arrival. Time in clinic = end - arrival. A patient who
    arrives before the window opens waits from their arrival, so the
    pre-window time counts as waiting.
14. Waiting room: the time-average number waiting over [window start, last
    consultation end], averaged over days. The daily peak is the maximum of
    that step function, averaged over days. Patients who arrive before the
    window opens are counted from the window start.
15. Doctor idle minutes = window minutes not spent consulting (including
    minutes after the last patient, if the session ends early). Overtime =
    max(0, last consultation end - window end).
16. Percentiles use statistics.quantiles(method="inclusive"). Shares are
    percentages of patients. Idle and overtime are per-day means in minutes.
17. Seed 20261128 for one random.Random stream shared by all doctors and days.
    N_DAYS = 4000 days per doctor.

Results are rounded to 1 decimal place. Shares are percentages.

How to run (from E:\\train):

    python -I docs/analysis/before_after_simulation.py

It prints a comparison table and writes docs/analysis/before_after_results.json
next to this script. Standard library only.
"""

import json
import math
import random
import statistics
import time
from pathlib import Path

SEED = 20261128
N_DAYS = 4000
BOOKING_PROB = 0.72
NO_SHOW_PROB = 0.10
WALKIN_TAIL_MIN = 30
SERVICE_MIN, SERVICE_MAX = 4.0, 55.0
AFTER_OFFSET_MEAN, AFTER_OFFSET_SD = -8.0, 9.0
AFTER_LATE_PROB = 0.12
AFTER_LATE_LO, AFTER_LATE_HI = 10.0, 30.0
BEFORE_CLAMP_LO, BEFORE_CLAMP_HI = -45.0, 180.0

# Active doctors, hard-coded from MediCare.Infrastructure/DemoData/DemoDataCatalog.cs.
# Window times are minutes after midnight (10:00 = 600). Source line numbers refer to
# the Doctors array in that file. Where a doctor has several windows of the same
# longest length, the first one listed is used.
DOCTORS = [
    # Dr Youssef El-Masry, Cardiology (lines 46-53). Slot 20 min, ServiceMeanFactor 1.08,
    # ServiceCv 0.45, WalkInsPerDay 1.75. Longest window Sun-Thu 10:00-14:00 (240 min);
    # tied with Tue/Thu 17:00-21:00, first listed used.
    {"key": "youssef", "name": "Dr Youssef El-Masry", "specialization": "Cardiology",
     "slot": 20, "factor": 1.08, "cv": 0.45, "walkins": 1.75,
     "window_start": 600, "window_end": 840},
    # Dr Mariam Hassan, Pediatrics (lines 54-58). Slot 15 min, factor 1.0, CV 0.52,
    # walk-ins 2.0. Window Sat-Thu 16:00-22:00 (360 min).
    {"key": "mariam", "name": "Dr Mariam Hassan", "specialization": "Pediatrics",
     "slot": 15, "factor": 1.0, "cv": 0.52, "walkins": 2.0,
     "window_start": 960, "window_end": 1320},
    # Dr Khaled Nabil, Orthopedics (lines 59-66). Slot 30 min, factor 1.1, CV 0.5,
    # walk-ins 1.5. Longest window Sun/Tue/Thu 12:00-18:00 (360 min); Sat 10:00-14:00 is shorter.
    {"key": "khaled", "name": "Dr Khaled Nabil", "specialization": "Orthopedics",
     "slot": 30, "factor": 1.1, "cv": 0.5, "walkins": 1.5,
     "window_start": 720, "window_end": 1080},
    # Dr Nadia Farouk, Dermatology (lines 67-71). Slot 20 min, factor 0.95, CV 0.4,
    # walk-ins 2.5. Window Sat/Mon/Tue/Wed 15:00-21:00 (360 min).
    {"key": "nadia", "name": "Dr Nadia Farouk", "specialization": "Dermatology",
     "slot": 20, "factor": 0.95, "cv": 0.4, "walkins": 2.5,
     "window_start": 900, "window_end": 1260},
    # Dr Amr Saleh, Internal Medicine (lines 72-79). Slot 20 min, factor 1.02, CV 0.55,
    # walk-ins 3.5. Longest window Sat-Thu 09:00-13:00 (240 min); tied with
    # Sat/Tue/Wed 18:00-22:00, first listed used.
    {"key": "amr", "name": "Dr Amr Saleh", "specialization": "Internal Medicine",
     "slot": 20, "factor": 1.02, "cv": 0.55, "walkins": 3.5,
     "window_start": 540, "window_end": 780},
    # Dr Laila Ibrahim, Ophthalmology (lines 80-84). Slot 20 min, factor 0.92, CV 0.38,
    # walk-ins 2.0. Window Sun/Tue/Thu 11:00-17:00 (360 min).
    {"key": "laila", "name": "Dr Laila Ibrahim", "specialization": "Ophthalmology",
     "slot": 20, "factor": 0.92, "cv": 0.38, "walkins": 2.0,
     "window_start": 660, "window_end": 1020},
    # Dr Hany Zaki (lines 85-89) is Active: false and is deliberately not listed.
]

# name -> (policy, (mean, sd) of the BEFORE arrival noise; unused for AFTER)
SCENARIOS = {
    "AFTER": ("after", None),
    "BEFORE": ("before", (20.0, 35.0)),
    "BEFORE narrow": ("before", (10.0, 20.0)),
    "BEFORE wide": ("before", (30.0, 50.0)),
}


def clamp(x, lo, hi):
    return max(lo, min(hi, x))


def lognormal_params(mean, cv):
    sigma2 = math.log(1.0 + cv * cv)
    return math.log(mean) - sigma2 / 2.0, math.sqrt(sigma2)


def draw_service(rng, mu, sigma):
    return clamp(rng.lognormvariate(mu, sigma), SERVICE_MIN, SERVICE_MAX)


def draw_poisson(rng, lam):
    """Knuth's multiplication method."""
    limit = math.exp(-lam)
    k = 0
    p = 1.0
    while True:
        p *= rng.random()
        if p <= limit:
            return k
        k += 1


def build_day(rng, doc):
    """Draw everything that is shared by all scenarios for one day of one doctor."""
    start, end, slot = doc["window_start"], doc["window_end"], doc["slot"]
    mu, sigma = lognormal_params(slot * doc["factor"], doc["cv"])
    booked = []
    slot_start = start
    while slot_start + slot <= end:
        is_booked = rng.random() < BOOKING_PROB
        no_show = rng.random() < NO_SHOW_PROB
        service = draw_service(rng, mu, sigma)
        z_after = rng.gauss(0.0, 1.0)
        late = rng.random() < AFTER_LATE_PROB
        late_amount = rng.uniform(AFTER_LATE_LO, AFTER_LATE_HI)
        z_before = rng.gauss(0.0, 1.0)
        if is_booked and not no_show:
            booked.append({"slot": float(slot_start), "service": service, "z_after": z_after,
                           "late": late, "late_amount": late_amount, "z_before": z_before})
        slot_start += slot

    walkins = []
    for _ in range(draw_poisson(rng, doc["walkins"])):
        service = draw_service(rng, mu, sigma)
        u = max(rng.random(), rng.random())
        walkins.append({"kind": "W", "slot": None, "service": service,
                        "arr": start + (end - start - WALKIN_TAIL_MIN) * u})
    return booked, walkins


def make_patients(booked, walkins, window_start, slot, policy, before_params):
    patients = list(walkins)
    for b in booked:
        if policy == "after":
            offset = AFTER_OFFSET_MEAN + AFTER_OFFSET_SD * b["z_after"]
            if b["late"]:
                offset += b["late_amount"]
            offset = clamp(offset, -45.0, 3 * slot - 5.0)
            arr = b["slot"] + offset
        else:
            mean, sd = before_params
            arr = window_start + clamp(mean + sd * b["z_before"], BEFORE_CLAMP_LO, BEFORE_CLAMP_HI)
        patients.append({"kind": "B", "slot": b["slot"], "service": b["service"], "arr": arr})
    return patients


def record(p, start, end):
    return {"kind": p["kind"], "arr": p["arr"], "start": start, "end": end, "service": p["service"]}


def serve_after(window_start, patients):
    """Timed-slot rule: earliest booked slot first, then earliest walk-in, else wait for next arrival."""
    pending = sorted(patients, key=lambda p: (p["arr"], p["kind"]))
    n = len(pending)
    waiting = []  # indices into pending, in arrival order
    nxt = 0
    free = float(window_start)
    out = []
    while len(out) < n:
        while nxt < n and pending[nxt]["arr"] <= free:
            waiting.append(nxt)
            nxt += 1
        if not waiting:
            free = pending[nxt]["arr"]
            continue
        booked_waiting = [i for i in waiting if pending[i]["kind"] == "B"]
        pick = min(booked_waiting, key=lambda i: pending[i]["slot"]) if booked_waiting else waiting[0]
        waiting.remove(pick)
        p = pending[pick]
        start = free
        free = start + p["service"]
        out.append(record(p, start, free))
    return out


def serve_before(window_start, patients):
    """First-come, first-served by arrival time, booked and walk-ins mixed."""
    free = float(window_start)
    out = []
    for p in sorted(patients, key=lambda p: (p["arr"], p["kind"])):
        start = max(free, p["arr"])
        free = start + p["service"]
        out.append(record(p, start, free))
    return out


class Acc:
    """Collects per-patient and per-day numbers for one scenario (overall or one doctor)."""

    def __init__(self):
        self.waits = {"all": [], "B": [], "W": []}
        self.tic = []
        self.day_n = []
        self.day_nb = []
        self.day_consult = []
        self.day_wr_avg = []
        self.day_wr_peak = []
        self.day_idle = []
        self.day_over = []

    def add_day(self, window_start, window_end, recs):
        self.day_n.append(len(recs))
        self.day_nb.append(sum(1 for r in recs if r["kind"] == "B"))
        self.day_consult.append(math.fsum(r["service"] for r in recs))
        t_end = max((r["end"] for r in recs), default=float(window_start))

        integral = 0.0
        events = []
        for r in recs:
            wait = r["start"] - r["arr"]
            assert wait >= 0.0, f"negative wait {wait}"
            assert r["start"] >= r["arr"] - 1e-9
            self.waits["all"].append(wait)
            self.waits[r["kind"]].append(wait)
            self.tic.append(r["end"] - r["arr"])
            from_t = max(r["arr"], window_start)
            if r["start"] > from_t:
                integral += r["start"] - from_t
                events.append((from_t, 1))
                events.append((r["start"], -1))
        events.sort(key=lambda e: (e[0], e[1]))  # at equal times, departures before arrivals
        cur = peak = 0
        for _, delta in events:
            cur += delta
            peak = max(peak, cur)
        duration = t_end - window_start
        self.day_wr_avg.append(integral / duration if duration > 0 else 0.0)
        self.day_wr_peak.append(peak)

        busy_in_window = sum(max(0.0, min(r["end"], window_end) - max(r["start"], window_start)) for r in recs)
        self.day_idle.append(max(0.0, (window_end - window_start) - busy_in_window))
        self.day_over.append(max(0.0, t_end - window_end))


def mean(xs):
    return statistics.fmean(xs) if xs else None


def wait_stats(xs):
    if not xs:
        return None
    p90 = statistics.quantiles(xs, n=10, method="inclusive")[8] if len(xs) >= 2 else xs[0]
    return {
        "mean": mean(xs),
        "median": statistics.median(xs),
        "p90": p90,
        "share_over_30_pct": 100.0 * sum(1 for x in xs if x > 30) / len(xs),
        "share_over_60_pct": 100.0 * sum(1 for x in xs if x > 60) / len(xs),
    }


def report(acc):
    walk_per_day = [n - nb for n, nb in zip(acc.day_n, acc.day_nb)]
    return {
        "patients_per_day": mean(acc.day_n),
        "booked_per_day": mean(acc.day_nb),
        "walkins_per_day": mean(walk_per_day),
        "consultation_min_per_day": mean(acc.day_consult),
        "wait_all": wait_stats(acc.waits["all"]),
        "wait_booked": wait_stats(acc.waits["B"]),
        "wait_walkin": wait_stats(acc.waits["W"]),
        "time_in_clinic_mean": mean(acc.tic),
        "waiting_room_time_avg_mean": mean(acc.day_wr_avg),
        "waiting_room_daily_peak_mean": mean(acc.day_wr_peak),
        "doctor_idle_min_per_day": mean(acc.day_idle),
        "doctor_overtime_min_per_day": mean(acc.day_over),
    }


def round_tree(x):
    if isinstance(x, dict):
        return {k: round_tree(v) for k, v in x.items()}
    if isinstance(x, float):
        return round(x, 1)
    return x


def self_test():
    """Tiny deterministic case: AFTER with arrivals at slot time and services shorter than the slot."""
    window_start, slot = 540, 20
    def waits_for(service):
        pats = [{"kind": "B", "slot": float(window_start + k * slot),
                 "arr": float(window_start + k * slot), "service": service} for k in range(3)]
        return [r["start"] - r["arr"] for r in serve_after(window_start, pats)]
    assert all(w == 0.0 for w in waits_for(15.0)), "AFTER waits should be 0 when services are shorter than the slot"
    assert max(waits_for(25.0)) > 0.0, "control case: a service longer than the slot must cause a wait"


def run_simulation():
    rng = random.Random(SEED)
    scen_names = list(SCENARIOS)
    overall = {s: Acc() for s in scen_names}
    per_doc = {d["key"]: {s: Acc() for s in scen_names} for d in DOCTORS}

    for doc in DOCTORS:
        ws, we, slot = doc["window_start"], doc["window_end"], doc["slot"]
        for _ in range(N_DAYS):
            booked, walkins = build_day(rng, doc)
            n_demand = len(booked) + len(walkins)
            demand_consult = math.fsum([b["service"] for b in booked] + [w["service"] for w in walkins])
            for name, (policy, before_params) in SCENARIOS.items():
                patients = make_patients(booked, walkins, ws, slot, policy, before_params)
                recs = serve_after(ws, patients) if policy == "after" else serve_before(ws, patients)
                # Self-checks: same patients per day, same consultation minutes per day.
                assert len(recs) == n_demand, f"{doc['key']} {name}: patient count differs"
                consult = math.fsum(r["service"] for r in recs)
                assert math.isclose(consult, demand_consult, rel_tol=1e-9, abs_tol=1e-9), \
                    f"{doc['key']} {name}: consultation minutes differ"
                overall[name].add_day(ws, we, recs)
                per_doc[doc["key"]][name].add_day(ws, we, recs)
    return overall, per_doc


def fmt(v):
    return "-" if v is None else f"{v:,.1f}"


def print_table(reports):
    cols = list(reports)
    rows = [
        ("Patients per day", lambda r: r["patients_per_day"]),
        ("  booked per day", lambda r: r["booked_per_day"]),
        ("  walk-ins per day", lambda r: r["walkins_per_day"]),
        ("Consultation minutes per day", lambda r: r["consultation_min_per_day"]),
        ("Wait all: mean (min)", lambda r: r["wait_all"]["mean"]),
        ("Wait all: median (min)", lambda r: r["wait_all"]["median"]),
        ("Wait all: P90 (min)", lambda r: r["wait_all"]["p90"]),
        ("Wait all: share >30 min (%)", lambda r: r["wait_all"]["share_over_30_pct"]),
        ("Wait all: share >60 min (%)", lambda r: r["wait_all"]["share_over_60_pct"]),
        ("Wait booked: mean (min)", lambda r: r["wait_booked"]["mean"]),
        ("Wait booked: P90 (min)", lambda r: r["wait_booked"]["p90"]),
        ("Wait booked: share >60 min (%)", lambda r: r["wait_booked"]["share_over_60_pct"]),
        ("Wait walk-in: mean (min)", lambda r: r["wait_walkin"]["mean"]),
        ("Wait walk-in: P90 (min)", lambda r: r["wait_walkin"]["p90"]),
        ("Wait walk-in: share >60 min (%)", lambda r: r["wait_walkin"]["share_over_60_pct"]),
        ("Time in clinic: mean (min)", lambda r: r["time_in_clinic_mean"]),
        ("Waiting room: time-avg waiting", lambda r: r["waiting_room_time_avg_mean"]),
        ("Waiting room: daily peak (mean)", lambda r: r["waiting_room_daily_peak_mean"]),
        ("Doctor idle min per day", lambda r: r["doctor_idle_min_per_day"]),
        ("Doctor overtime min per day", lambda r: r["doctor_overtime_min_per_day"]),
    ]
    label_w = 34
    col_w = 15
    print("SIMULATED RESULTS (not measured clinic data)")
    print("Metric".ljust(label_w) + "".join(c.rjust(col_w) for c in cols))
    print("-" * (label_w + col_w * len(cols)))
    for label, get in rows:
        print(label.ljust(label_w) + "".join(fmt(get(reports[c])).rjust(col_w) for c in cols))


def print_per_doctor(per_doc_reports):
    print()
    print("Per doctor: wait mean / P90 / share >60 min (%), AFTER vs BEFORE")
    header = "Doctor".ljust(22) + "".join(h.rjust(12) for h in
              ["A mean", "B mean", "A P90", "B P90", "A >60%", "B >60%"])
    print(header)
    print("-" * len(header))
    for name, rep in per_doc_reports.items():
        a, b = rep["AFTER"]["wait_all"], rep["BEFORE"]["wait_all"]
        vals = [a["mean"], b["mean"], a["p90"], b["p90"], a["share_over_60_pct"], b["share_over_60_pct"]]
        print(name.ljust(22) + "".join(fmt(v).rjust(12) for v in vals))


def main():
    started = time.perf_counter()
    self_test()
    overall, per_doc = run_simulation()

    # Cross-scenario checks on the aggregated per-day counts and consultation minutes.
    ref_n = overall["AFTER"].day_n
    ref_c = overall["AFTER"].day_consult
    for name in SCENARIOS:
        assert overall[name].day_n == ref_n, f"patients per day differ for {name}"
        assert all(math.isclose(a, b, rel_tol=1e-9, abs_tol=1e-9)
                   for a, b in zip(overall[name].day_consult, ref_c)), f"consultation minutes differ for {name}"
        assert min(overall[name].waits["all"]) >= 0.0, f"negative wait in {name}"

    reports = {name: report(overall[name]) for name in SCENARIOS}
    per_doc_reports = {}
    for doc in DOCTORS:
        per_doc_reports[doc["name"]] = {s: report(per_doc[doc["key"]][s]) for s in ("AFTER", "BEFORE")}

    elapsed = time.perf_counter() - started
    print_table(reports)
    print_per_doctor(per_doc_reports)
    print()
    print(f"Days per doctor: {N_DAYS}, doctors: {len(DOCTORS)}, seed: {SEED}")
    print("Self-checks passed: identical patients/day, identical consultation minutes/day, no negative waits, unit case.")
    print(f"Runtime: {elapsed:.2f} s")

    result = {
        "note": "SIMULATED RESULTS. Not measured clinic data. See the module docstring for assumptions.",
        "seed": SEED,
        "days_per_doctor": N_DAYS,
        "doctors": [{"name": d["name"], "specialization": d["specialization"], "slot_min": d["slot"],
                     "service_mean_factor": d["factor"], "service_cv": d["cv"],
                     "walkins_per_day": d["walkins"],
                     "longest_window": f"{d['window_start'] // 60:02d}:{d['window_start'] % 60:02d}-"
                                       f"{d['window_end'] // 60:02d}:{d['window_end'] % 60:02d}"}
                    for d in DOCTORS],
        "scenarios": reports,
        "sensitivity_note": "BEFORE narrow = Normal(+10, 20) arrival noise; BEFORE wide = Normal(+30, 50). "
                            "AFTER and BEFORE use Normal(-8, 9) plus late tail and Normal(+20, 35) respectively.",
        "per_doctor": per_doc_reports,
        "checks": {"patients_per_day_identical": True, "consultation_minutes_identical": True,
                   "no_negative_waits": True, "unit_case_zero_waits": True},
    }
    out_path = Path(__file__).resolve().parent / "before_after_results.json"
    out_path.write_text(json.dumps(round_tree(result), indent=2), encoding="utf-8")
    print(f"Wrote {out_path}")


if __name__ == "__main__":
    main()
