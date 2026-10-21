#!/usr/bin/env python3
"""A6-01: time customer search, account search and account opening against a running API.

Usage (API running against a database filled with `dotnet run --project src/Banking.Migrator -- perf`):

    MEASURE_STAFF_CODE=ADMIN MEASURE_SECRET='<password>' python3 scripts/measure-performance.py

Optional: MEASURE_URL (default http://localhost:5080), MEASURE_RUNS (default 50 per scenario),
MEASURE_OPENS (default 30 account openings). The staff member needs customer and account view rights, and account
opening rights in the working branch, which must have an open business day. Opening adds real accounts, so run it
only against a performance database.

Prints p50, p95 and max per scenario in milliseconds, as Markdown rows for docs/performance.md. Uses only the
standard library. Times are measured at the client, so they include the network hop; run it on the API host or next
to it.
"""

import json
import os
import statistics
import sys
import time
import urllib.error
import urllib.request

BASE = os.environ.get("MEASURE_URL", "http://localhost:5080").rstrip("/")
RUNS = int(os.environ.get("MEASURE_RUNS", "50"))
OPENS = int(os.environ.get("MEASURE_OPENS", "30"))
WARM_UP = 5

TARGETS_MS = {"search": 300, "open": 500}


def call(method, path, body=None, token=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(f"{BASE}{path}", data=data, method=method)
    request.add_header("Accept", "application/json")
    if data is not None:
        request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            payload = response.read()
            status = response.status
    except urllib.error.HTTPError as error:
        payload = error.read()
        status = error.code
    elapsed = (time.perf_counter() - started) * 1000
    return status, (json.loads(payload) if payload else None), elapsed


def percentile(values, share):
    ordered = sorted(values)
    index = max(0, min(len(ordered) - 1, round(share * len(ordered)) - 1))
    return ordered[index]


def measure(name, kind, request, runs):
    for _ in range(WARM_UP):
        request()
    times = []
    for _ in range(runs):
        status, _, elapsed = request()
        if status >= 400:
            sys.exit(f"{name}: HTTP {status}")
        times.append(elapsed)
    p95 = percentile(times, 0.95)
    verdict = "pass" if p95 < TARGETS_MS[kind] else "FAIL"
    print(f"| {name} | {runs} | {statistics.median(times):.0f} | {p95:.0f} | {max(times):.0f} | < {TARGETS_MS[kind]} | {verdict} |")


def main():
    code = os.environ.get("MEASURE_STAFF_CODE")
    secret = os.environ.get("MEASURE_SECRET")
    if not code or not secret:
        sys.exit("Set MEASURE_STAFF_CODE and MEASURE_SECRET.")

    status, login, _ = call("POST", "/api/v1/auth/login", {"staffCode": code, "secretKey": secret})
    if status != 200:
        sys.exit(f"Sign-in failed: HTTP {status}")
    token = login["accessToken"]

    print("| Scenario | Runs | p50 ms | p95 ms | max ms | Target p95 | Result |")
    print("| --- | --- | --- | --- | --- | --- | --- |")

    searches = [
        ("Customer search: common family name", {"q": "Mensah", "page": 1, "pageSize": 25}),
        ("Customer search: full name", {"q": "Kwame Mensah", "page": 1, "pageSize": 25}),
        ("Customer search: customer number", {"q": "00100000", "page": 1, "pageSize": 25}),
        ("Customer search: mobile number", {"q": "0241234567", "page": 1, "pageSize": 25}),
        ("Customer search: pending KYC filter", {"kycStatus": "PENDING", "page": 1, "pageSize": 25}),
        ("Customer search: page 200", {"q": "Mensah", "page": 200, "pageSize": 25}),
    ]
    for name, body in searches:
        measure(name, "search", lambda body=body: call("POST", "/api/v1/parties/search", body, token), RUNS)

    accounts = [
        ("Account search: name", {"q": "Boateng", "page": 1, "pageSize": 25}),
        ("Account search: account number prefix", {"q": "1000001", "page": 1, "pageSize": 25}),
        ("Account search: frozen filter", {"status": "FROZEN", "page": 1, "pageSize": 25}),
    ]
    for name, body in accounts:
        measure(name, "search", lambda body=body: call("POST", "/api/v1/accounts/search", body, token), RUNS)

    if OPENS <= 0:
        return

    _, roles, _ = call("GET", "/api/v1/lookups/account-holder-roles", token=token)
    _, modes, _ = call("GET", "/api/v1/lookups/operating-modes", token=token)
    _, products, _ = call("GET", "/api/v1/products?openable=true&pageSize=50", token=token)
    primary = next(row["id"] for row in roles if row["code"] == "PRIMARY")
    single = next(row["id"] for row in modes if row["code"] == "SINGLE")
    savings = next(row["productId"] for row in products["items"] if row["productCode"] == "SAV")

    # Fresh holders each time: approved, verified individuals with a signature.
    _, found, _ = call(
        "POST",
        "/api/v1/parties/search",
        {"partyType": "INDIVIDUAL", "kycStatus": "VERIFIED", "oldestFirst": True, "page": 1, "pageSize": 100},
        token,
    )
    holders = [row["partyId"] for row in found["items"] if row.get("canBePrimaryHolder")]
    needed = OPENS + WARM_UP
    if len(holders) < needed:
        sys.exit(f"Account opening: need {needed} eligible customers, found {len(holders)}.")
    pool = iter(holders)

    def open_account():
        body = {"productId": savings, "operatingModeId": single, "holders": [{"partyId": next(pool), "accountHolderRoleId": primary}]}
        return call("POST", "/api/v1/accounts", body, token)

    measure("Open account (single holder)", "open", open_account, OPENS)


if __name__ == "__main__":
    main()
