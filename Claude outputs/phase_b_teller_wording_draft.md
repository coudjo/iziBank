# Phase B teller screens: wording draft (BF1 to BF3)

Draft for owner approval. Built from the Phase A conventions (sentence case, no codes, staff terms: customer, approve, awaiting approval). Nothing is built until this is approved. Items marked **[confirm]** need a decision.

## Conventions used

- Staff terms: till, cash deposit, cash withdrawal, transfer, approve, awaiting approval.
- Amounts: `GHS 1,250.00` **[confirm currency label and format]**.
- Waiting for approval is a banner, never an error: "Awaiting approval. The cash is not recorded until a supervisor approves it."
- Buttons lock while a request runs. A timeout uses the Phase A message: "We could not confirm this was saved. Check before trying again."

## BF1: Teller home (`/teller`)

**Page title:** Teller

**Till card, no till open**
- Heading: Your till
- Text: "No till is open for today."
- Button: **Open till** (disabled with the reason if the business day is not open: "The business day is not open for this branch. Ask a supervisor to open it.")

**Till card, till open**
- Heading: Your till
- Text: "Open since {time}."
- Row: Till balance `GHS 12,500.00`
- Buttons: **Buy cash**, **Sell cash**, **Close till**

**Today's totals** (four figures, each a count and an amount)
- Cash deposits
- Cash withdrawals
- Transfers
- Cash bought and sold

**Open till dialog**
- Title: Open till
- Text: "Your till opens with the balance you carry in." **[confirm: whether the opening balance is entered here or always starts at zero]**
- Button: **Open till**

**Buy cash / Sell cash dialog**
- Title: Buy cash from vault / Sell cash to vault
- Field: Amount (must be greater than zero: "Enter an amount greater than zero.")
- Button: **Request from vault** / **Send to vault**
- Result banner: "Awaiting approval. Your till balance changes when a vault supervisor approves it."

**Close till dialog**
- Title: Close till
- Text: "Count the cash in your till, then enter the amount."
- Field: Counted amount
- If the counted amount differs from the till balance, a notice appears before confirming:
  - Short: "You counted GHS 10.00 short. The difference will be recorded as cash short for approval."
  - Over: "You counted GHS 10.00 over. The difference will be recorded as cash over for approval."
- Button: **Close till**
- Result: "Till closed. Waiting for approval of the difference." (when a difference exists) or "Till closed." (when it balances)

**Errors on this page**
- A till is already open: "You already have a till open for today."
- No till account set up: "Your till account is not set up. Ask a supervisor to set it up."
- Business day not open: as above.

## BF2: Cash deposit and cash withdrawal (`/teller/deposit`, `/teller/withdrawal`)

Single page, four steps shown as a list at the top.

**Step 1: Account**
- Search field: "Account number or customer name"
- Results show account number, holder name and state. Accounts that cannot take this transaction appear disabled with a reason:
  - Frozen: "This account is frozen."
  - Closed: "This account is closed."
  - Not approved yet: "This account is awaiting approval."
- Button on a selected account: **Continue**

**Step 2: Amount and people**
- Field: Amount (required; "Enter an amount greater than zero.")
- Field: Narration (optional)
- **Deposit only:** section "Who is paying in?"
  - Name, mobile, ID type and ID number **[confirm: which are required]**
  - Checkbox: "The account holder is paying in" (hides the section)
- **Withdrawal only:** section "Who is collecting?"
  - Checkbox: "The account holder is collecting" (hides the section)
  - Otherwise name, mobile and ID fields as for deposit
- **Withdrawal, signatures:** section "Who is signing?"
  - Mandate line from the account, e.g. "All to sign: 2 signers must confirm." or "Either to sign: 1 signer must confirm."
  - One checkbox per signer: "{name} has signed" **[confirm: how the signature is checked]**
  - If the mandate is not met: "Every signer must confirm before this withdrawal can continue."

**Step 3: Quote**
- Shows: Amount, Charge, Total. Charge line reads "No charge" when zero.
- Button: **Continue**

**Step 4: Confirm**
- Summary of the account, amount and charge
- Button: **Confirm deposit** / **Confirm withdrawal**
- Button locks while the request runs.

**Outcomes**
- Recorded: go to the receipt.
- Above the teller limit: "This amount is above your limit. A supervisor must approve it before the cash is recorded." Banner and the receipt shows "Awaiting approval".
- Insufficient money: "The account does not have enough money for this withdrawal."

**Receipt**
- Heading: Receipt
- Lines: account number, holder, type, amount, charge, narration, date and time, staff code, receipt number.
- Buttons: **Print receipt**, **New deposit** (or **New withdrawal**), **Done**
- Banner when pending: "Awaiting approval. The cash is not recorded until a supervisor approves it."

## BF3: Transfer (`/teller/transfer`)

**Step 1: From and to**
- From account: search, as BF2 step 1 (the account must be able to send)
- To account: search, as BF2 step 1, across all branches. A branch other than the teller's shows "Held at {branch name}".
- Error if both match: "Choose a different account for the receiver."

**Step 2: Amount and signatures**
- Amount, Narration
- Signers for the sending account, as BF2 step 2

**Step 3: Quote**
- Amount, Charge, Total, shown as BF2

**Step 4: Confirm**
- Summary: "Transfer {amount} from {from account} to {to account}."
- Button: **Confirm transfer**

**Outcomes**
- Posted: receipt with both accounts.
- Awaiting approval: as BF2.

## Open decisions

1. Currency label and format: `GHS 1,250.00`?
2. Which ID fields are required for a person paying in or collecting cash?
3. How a signer's confirmation is recorded (tick box, or checked against the specimen signature on file)?
4. Print: browser print of the receipt page, or a PDF?
5. Opening balance when a till opens: entered, or always zero?
6. Which role sees **Buy cash** and **Sell cash**?
