"""
Slot-length policy SIMULATION for the MediCare graduation project.

IMPORTANT: every number this script prints or writes is SIMULATED. There is no
real clinic data behind it. It asks whether each doctor's appointment slot
length should be set from that doctor's mean consultation length, under the
AFTER (timed-slot, system) queue rules from before_after_simulation.py.

Policies compared (all under the AFTER rules, same demand in every policy)

  CURRENT        slot length as now. Under this policy the mapping below is the
                 identity, so the results reproduce before_after_simulation.py's
                 AFTER numbers exactly.
  MEAN           slot = doctor's true mean consultation (slot x ServiceMeanFactor)
                 rounded to the nearest 5 min (minimum 10).
  MEAN_EXACT     slot = the same true mean rounded to the nearest 1 min (minimum
                 10). Checks whether rounding to 5 min hides the effect.
  MEAN_MINUS_5   MEAN - 5 min (minimum 10).
  MEAN_PLUS_5    MEAN + 5 min.
  CURRENT_PACKED EXTRA, clearly labelled, not a candidate. "Book consecutive
                 slots from the window start": request i takes slot i of the
                 current-length grid. It is a policy in its own right, not a
                 control for the mapping below.

Slot assignment rule (every policy except CURRENT_PACKED, CURRENT included)

  Take that day's booked requests in order of their original CURRENT slot time.
  Give each request the free slot in the policy's grid (starts at window start,
  start + L, ..., with slot end <= window end) whose start is nearest to its
  original CURRENT slot time. A tie goes to the earlier slot. If no free slot is
  left, the request is turned away. The patient's arrival offset is applied to
  the NEW slot time. Under CURRENT this rule is the identity: every request keeps
  its own slot. So the only thing that changes between the policies is slot length
  (and therefore the grid the requests are moved onto).

Assumptions (plain English)

 1. Doctors, consultation distribution, walk-in model, AFTER arrival offset and
    AFTER serving rule are imported from docs/analysis/before_after_simulation.py
    (same folder). It is loaded by file path with bytecode writing off. Its
    main() is not called, so its JSON file is not touched. The doctor list is the
    six ACTIVE doctors from MediCare.Infrastructure/DemoData/DemoDataCatalog.cs,
    one session per doctor per day (the longest weekly window).
 2. Demand is fixed. Each day, the booking requests are drawn under CURRENT exactly
    as before_after_simulation.py does: each CURRENT slot is booked with
    probability 0.72, no-show with probability 0.10. R = number of booked,
    non-no-show requests = that day's demand. Every policy receives the same R
    requests, with the same consultation lengths, no-show flags and standard-normal
    arrival noise.
 3. Consultation lengths do NOT depend on the slot length. This is an assumption.
    In reality doctors may speed up when slots are short, which would favour
    shorter slots. The effect is not modelled.
 4. Turned away: a request with no free slot left in the policy's grid is not seen.
    It is not in any wait statistic, its consultation is not used, and it is counted
    per day as a booked patient turned away.
 5. AFTER arrival: NEW slot time + offset, offset ~ Normal(-8, 9) min, plus with
    probability 0.12 an extra U(10, 30) min, clamped to [-45, 3 x slot - 5], using
    the slot length of the policy being run.
 6. Walk-ins are identical in every policy (same count, same times, same lengths).
 7. Serving rule: waiting booked patients by slot time first, then walk-ins by
    arrival. The doctor starts at the window start, serves without interruption,
    and keeps going until everyone has been seen.
 8. Wait = start - arrival (time before window start counts as waiting). Doctor
    idle minutes = window minutes not spent consulting. Overtime = max(0, last
    consultation end - window end).
 9. Overall figures pool all patients over all doctor-days. Per-day figures are
    averages per doctor-day. Percentiles use statistics.quantiles(method="inclusive").
10. Common random numbers: one random.Random stream with seed 20261128, N_DAYS =
    4000 days per doctor, drawn in the same order as before_after_simulation.py.
    The CURRENT policy therefore reproduces its AFTER numbers exactly.

Self-checks (asserted): CURRENT mapping is the identity (no turned-away, every
request keeps its slot); CURRENT patients per day and mean wait match the AFTER
numbers from before_after_simulation.py (recomputed in memory and against its JSON
file) within 0.1; demand R is identical across policies (seen booked + turned away
= R every day); walk-in counts are identical; no negative waits.

Results are rounded to 1 decimal place. Shares are percentages.

How to run (from E:\\train):

    python -I docs/analysis/slot_length_simulation.py

It prints an overall table and a per-doctor table and writes
docs/analysis/slot_length_results.json next to this script. Standard library only.
"""

import importlib.util
import json
import math
import random
import statistics
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent

# Load the sibling module by path. Bytecode writing is off so no __pycache__ is created.
sys.dont_write_bytecode = True
_spec = importlib.util.spec_from_file_location(
    "before_after_simulation", HERE / "before_after_simulation.py")
ba = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ba)

SEED = ba.SEED
N_DAYS = ba.N_DAYS
MIN_SLOT = 10
POLICIES = ["CURRENT", "MEAN", "MEAN_EXACT", "MEAN_MINUS_5", "MEAN_PLUS_5", "CURRENT_PACKED"]
EXTRA_POLICY = "CURRENT_PACKED"


def policy_slots(doc):
    """Slot length in minutes for each policy, for one doctor."""
    raw_mean = doc["slot"] * doc["factor"]
    mean_5 = max(MIN_SLOT, int(math.floor(raw_mean / 5.0 + 0.5)) * 5)
    mean_exact = max(MIN_SLOT, int(math.floor(raw_mean + 0.5)))
    return {
        "CURRENT": doc["slot"],
        "MEAN": mean_5,
        "MEAN_EXACT": mean_exact,
        "MEAN_MINUS_5": max(MIN_SLOT, mean_5 - 5),
        "MEAN_PLUS_5": mean_5 + 5,
        "CURRENT_PACKED": doc["slot"],
    }


def nearest_free_slots(booked, ws, we, slot):
    """Each request, in original slot order, takes the nearest free grid slot (ties: earlier)."""
    n_slots = (we - ws) // slot
    taken = [False] * n_slots
    kept, turned = [], []
    for b in booked:
        best, best_d = None, None
        for k in range(n_slots):
            if taken[k]:
                continue
            d = abs(ws + k * slot - b["slot"])
            if best_d is None or d < best_d:  # strict, so the earlier slot wins ties
                best, best_d = k, d
        if best is None:
            turned.append(b)
        else:
            taken[best] = True
            kept.append(dict(b, slot=float(ws + best * slot)))
    return kept, turned


def consecutive_from_start(booked, ws, we, slot):
    """EXTRA policy: request i takes slot i of the grid from the window start."""
    n_slots = (we - ws) // slot
    kept = [dict(b, slot=float(ws + i * slot)) for i, b in enumerate(booked[:n_slots])]
    return kept, booked[n_slots:]


def run_policy(policy, doc, slot, booked, walkins):
    """Return (records, kept_requests, turned_away_requests) for one policy on one day."""
    ws, we = doc["window_start"], doc["window_end"]
    if policy == EXTRA_POLICY:
        kept, turned = consecutive_from_start(booked, ws, we, slot)
    else:
        kept, turned = nearest_free_slots(booked, ws, we, slot)
    patients = ba.make_patients(kept, walkins, ws, slot, "after", None)
    return ba.serve_after(ws, patients), kept, turned


def summarize(acc, turned_counts, slot_mean):
    return {
        "slot_min_mean": slot_mean,
        "patients_seen_per_day": ba.mean(acc.day_n),
        "booked_seen_per_day": ba.mean(acc.day_nb),
        "booked_turned_away_per_day": ba.mean(turned_counts),
        "wait_all": ba.wait_stats(acc.waits["all"]),
        "wait_booked": ba.wait_stats(acc.waits["B"]),
        "wait_walkin": ba.wait_stats(acc.waits["W"]),
        "doctor_idle_min_per_day": ba.mean(acc.day_idle),
        "doctor_overtime_min_per_day": ba.mean(acc.day_over),
    }


def run_simulation():
    rng = random.Random(SEED)
    overall = {p: ba.Acc() for p in POLICIES}
    overall_turned = {p: [] for p in POLICIES}
    overall_slots = {p: [] for p in POLICIES}
    per_doc = {}
    slot_by_doc = {}

    for doc in ba.DOCTORS:
        ws, we = doc["window_start"], doc["window_end"]
        slots = policy_slots(doc)
        slot_by_doc[doc["key"]] = slots
        per_doc[doc["key"]] = {
            "acc": {p: ba.Acc() for p in POLICIES},
            "turned": {p: [] for p in POLICIES},
        }
        for _ in range(N_DAYS):
            booked, walkins = ba.build_day(rng, doc)
            demand = len(booked)  # R: identical for every policy
            booked_service = math.fsum(b["service"] for b in booked)
            for p in POLICIES:
                recs, kept, turned = run_policy(p, doc, slots[p], booked, walkins)
                if p == "CURRENT":
                    # Identity mapping: every request keeps its original slot, nobody is turned away.
                    assert not turned, f"{doc['key']}: CURRENT turned a request away"
                    assert [k["slot"] for k in kept] == [b["slot"] for b in booked], \
                        f"{doc['key']}: CURRENT mapping is not the identity"
                seen_booked = [r for r in recs if r["kind"] == "B"]
                assert len(seen_booked) + len(turned) == demand, f"{doc['key']} {p}: demand not conserved"
                assert len(recs) - len(seen_booked) == len(walkins), f"{doc['key']} {p}: walk-ins differ"
                seen_service = math.fsum(r["service"] for r in seen_booked)
                turned_service = math.fsum(b["service"] for b in turned)
                assert math.isclose(seen_service + turned_service, booked_service,
                                    rel_tol=1e-9, abs_tol=1e-9), f"{doc['key']} {p}: service differs"
                overall[p].add_day(ws, we, recs)
                per_doc[doc["key"]]["acc"][p].add_day(ws, we, recs)
                overall_turned[p].append(len(turned))
                per_doc[doc["key"]]["turned"][p].append(len(turned))
        for p in POLICIES:
            overall_slots[p].extend([slots[p]] * N_DAYS)
    return overall, overall_turned, overall_slots, per_doc, slot_by_doc


def fmt(v):
    return "-" if v is None else f"{v:,.1f}"


def print_overall(reports):
    cols = POLICIES
    rows = [
        ("Slot length, mean over doctors (min)", lambda r: r["slot_min_mean"]),
        ("Patients seen per doctor-day", lambda r: r["patients_seen_per_day"]),
        ("  booked seen per day", lambda r: r["booked_seen_per_day"]),
        ("Booked turned away per day", lambda r: r["booked_turned_away_per_day"]),
        ("Wait all: mean (min)", lambda r: r["wait_all"]["mean"]),
        ("Wait all: median (min)", lambda r: r["wait_all"]["median"]),
        ("Wait all: P90 (min)", lambda r: r["wait_all"]["p90"]),
        ("Wait all: share >30 min (%)", lambda r: r["wait_all"]["share_over_30_pct"]),
        ("Wait booked: mean (min)", lambda r: r["wait_booked"]["mean"]),
        ("Wait booked: share >30 min (%)", lambda r: r["wait_booked"]["share_over_30_pct"]),
        ("Wait walk-in: mean (min)", lambda r: r["wait_walkin"]["mean"]),
        ("Wait walk-in: share >30 min (%)", lambda r: r["wait_walkin"]["share_over_30_pct"]),
        ("Doctor idle min per day", lambda r: r["doctor_idle_min_per_day"]),
        ("Doctor overtime min per day", lambda r: r["doctor_overtime_min_per_day"]),
    ]
    label_w, col_w = 36, 16
    print("SIMULATED RESULTS (not measured clinic data). All under AFTER rules.")
    print("Mapping: nearest free slot to each request's CURRENT slot time (ties: earlier).")
    print("CURRENT_PACKED is an EXTRA column: consecutive slots from window start, not a candidate.")
    print("Metric".ljust(label_w) + "".join(c.rjust(col_w) for c in cols))
    print("-" * (label_w + col_w * len(cols)))
    for label, get in rows:
        print(label.ljust(label_w) + "".join(fmt(get(reports[c])).rjust(col_w) for c in cols))


def print_per_doctor(per_doc_reports):
    print()
    print("Per doctor. C = CURRENT, M = MEAN (5-min), E = MEAN_EXACT (1-min). Wait = mean wait, all patients (min).")
    headers = ["Slot C", "Slot M", "Slot E", "Wait C", "Wait M", "Wait E",
               "OT C", "OT M", "OT E", "Idle C", "Idle M", "Idle E",
               "Turn C", "Turn M", "Turn E"]
    w = 9
    head = "Doctor".ljust(22) + "".join(h.rjust(w) for h in headers)
    print(head)
    print("-" * len(head))
    for name, rep in per_doc_reports.items():
        c, m, e = rep["CURRENT"], rep["MEAN"], rep["MEAN_EXACT"]
        vals = [
            c["slot_min_mean"], m["slot_min_mean"], e["slot_min_mean"],
            c["wait_all"]["mean"], m["wait_all"]["mean"], e["wait_all"]["mean"],
            c["doctor_overtime_min_per_day"], m["doctor_overtime_min_per_day"], e["doctor_overtime_min_per_day"],
            c["doctor_idle_min_per_day"], m["doctor_idle_min_per_day"], e["doctor_idle_min_per_day"],
            c["booked_turned_away_per_day"], m["booked_turned_away_per_day"], e["booked_turned_away_per_day"],
        ]
        print(name.ljust(22) + "".join(fmt(v).rjust(w) for v in vals))


def round_tree(x):
    return ba.round_tree(x)


def main():
    started = time.perf_counter()
    ba.self_test()

    overall, overall_turned, overall_slots, per_doc, slot_by_doc = run_simulation()

    overall_reports = {}
    for p in POLICIES:
        slot_mean = statistics.fmean(overall_slots[p])
        overall_reports[p] = summarize(overall[p], overall_turned[p], slot_mean)

    per_doc_reports = {}
    for doc in ba.DOCTORS:
        d = per_doc[doc["key"]]
        per_doc_reports[doc["name"]] = {
            p: dict(summarize(d["acc"][p], d["turned"][p], slot_by_doc[doc["key"]][p]),
                    slot_min=slot_by_doc[doc["key"]][p])
            for p in POLICIES
        }

    # Self-check 1: CURRENT reproduces before_after_simulation.py's AFTER numbers.
    ba_overall, _ = ba.run_simulation()
    ba_after = ba.report(ba_overall["AFTER"])
    cur = overall_reports["CURRENT"]
    assert abs(cur["patients_seen_per_day"] - ba_after["patients_per_day"]) <= 0.1, \
        f"patients/day: {cur['patients_seen_per_day']} vs {ba_after['patients_per_day']}"
    assert abs(cur["wait_all"]["mean"] - ba_after["wait_all"]["mean"]) <= 0.1, \
        f"mean wait: {cur['wait_all']['mean']} vs {ba_after['wait_all']['mean']}"
    json_path = HERE / "before_after_results.json"
    if json_path.exists():
        saved = json.loads(json_path.read_text(encoding="utf-8"))["scenarios"]["AFTER"]
        assert abs(cur["patients_seen_per_day"] - saved["patients_per_day"]) <= 0.1, "differs from saved JSON"
        assert abs(cur["wait_all"]["mean"] - saved["wait_all"]["mean"]) <= 0.1, "wait differs from saved JSON"

    # Self-check 2: no negative waits (Acc also asserts per patient).
    for p in POLICIES:
        assert min(overall[p].waits["all"]) >= 0.0, f"negative wait under {p}"

    elapsed = time.perf_counter() - started
    print_overall(overall_reports)
    print_per_doctor(per_doc_reports)
    print()
    print(f"Days per doctor: {N_DAYS}, doctors: {len(ba.DOCTORS)}, seed: {SEED}")
    print("Self-checks passed: CURRENT mapping is identity and matches before_after AFTER (patients/day, "
          "mean wait, saved JSON); demand conserved per day; walk-ins identical; no negative waits.")
    print(f"Runtime: {elapsed:.2f} s")

    result = {
        "note": "SIMULATED RESULTS. Not measured clinic data. See the module docstring for assumptions.",
        "question": "Should each doctor's slot length be set from the measured mean consultation length?",
        "seed": SEED,
        "days_per_doctor": N_DAYS,
        "assignment_rule": "Requests in original CURRENT slot order each take the nearest free slot in the "
                           "policy's grid (tie: earlier); none left = turned away. Arrival offset applied to "
                           "the new slot time. Identity under CURRENT.",
        "policies": {
            "CURRENT": "current slot length",
            "MEAN": "slot x ServiceMeanFactor rounded to nearest 5 min (min 10)",
            "MEAN_EXACT": "slot x ServiceMeanFactor rounded to nearest 1 min (min 10)",
            "MEAN_MINUS_5": "MEAN - 5 min (min 10)",
            "MEAN_PLUS_5": "MEAN + 5 min",
            "CURRENT_PACKED": "EXTRA, not a candidate: current slot length, requests booked in consecutive "
                              "slots from the window start",
        },
        "doctors": [{"name": d["name"], "key": d["key"], "specialization": d["specialization"],
                     "slot_current_min": d["slot"],
                     "slot_mean_min": slot_by_doc[d["key"]]["MEAN"],
                     "slot_mean_exact_min": slot_by_doc[d["key"]]["MEAN_EXACT"],
                     "slot_mean_minus_5_min": slot_by_doc[d["key"]]["MEAN_MINUS_5"],
                     "slot_mean_plus_5_min": slot_by_doc[d["key"]]["MEAN_PLUS_5"],
                     "service_mean_factor": d["factor"], "service_cv": d["cv"],
                     "walkins_per_day": d["walkins"]}
                    for d in ba.DOCTORS],
        "overall": overall_reports,
        "per_doctor": per_doc_reports,
        "checks": {"cur_mapping_is_identity": True, "cur_matches_before_after_after": True,
                   "demand_conserved": True, "walkins_identical": True, "no_negative_waits": True},
        "runtime_seconds": elapsed,
    }
    out_path = HERE / "slot_length_results.json"
    out_path.write_text(json.dumps(round_tree(result), indent=2), encoding="utf-8")
    print(f"Wrote {out_path}")


if __name__ == "__main__":
    main()
