# Platform workbook certification

This manifest records the May 2026 source workbooks used to design the first versioned import adapters. The source files contain personal and banking data and are intentionally not committed to the repository. Production import certification must compare the uploaded SHA-256 value with the approved template version before approval.

## Inspection boundary

The Keeta segment/order-detail tab contains approximately 162,873 rows. At the owner's direction, only its header and a small first-row sample were inspected. The application adapter itself remains streaming and preserves all non-empty cells when a production file is actually imported; this certification review did not load the full detail tab into memory.

| Adapter | File size | SHA-256 | Certified sheets and controls |
|---|---:|---|---|
| `amazon-anow-v1` | 18,656 bytes | `7DA8D9FDAAB2C354FE4B4FABE1CA801615BE54A28D32484476E882106D595FE5` | `Sheet1` (`A1:AU33`): external worker ID, Iqama, rider/name, DSP/store, daily columns, grand total, working days, days off, prorated amount, shipment incentive and Eid fields. Amount columns are company evidence and are not an active rider compensation policy. |
| `keeta-pay-per-order-v1` | 183,487 bytes | `60DAEDFC257743660B362380DB6AF7CB283D373BE9359B20C7861E98FE4602F6` | Partner summary (`A1:V2`), rider summary (`A1:V11`), order detail (`A1:J4349`). Certified controls include delivery fee `45,899.53`, VAT `6,914.33`, and company total `53,009.88`. |
| `keeta-segments-v1` | 11,237,627 bytes | `EC20FA79FB2949065D7431665BB2673727AA4E571AF2E216678858EB651D6243` | Partner summary (2 rows/19 columns), rider summary (152 rows/26 columns), and sampled order-detail header/first record only. Certified company control `1,122,463.40`, VAT `146,930.01`. Rider validity includes Arabic valid/invalid values and a reason column. |
| `hunger-ftr-v1` | 25,503 bytes | `31F18477F8C66BB234C8B202164EEC92A55BBC33999BB5FF42C623919B634B0B` | `WR` company totals, `RLVL` rider summary (102 riders), `FTR Cost`, and `Car Rental`. Certified company total including VAT `743,717.8987`. |

## Accounting interpretation

- Company billing, VAT, incentives, penalties, and platform settlement controls are normalized as separate facts. They are never silently treated as rider compensation.
- Rider compensation is calculated only from an active, effective-dated accountant policy after the import is reconciled and approved.
- Keeta segment validity is a blocking fact. An override is a separate immutable record with reason, actor, and timestamp.
- A workbook upload, parse, reconciliation, or approval never creates payroll or a journal by itself.
- Unknown or additional columns remain raw evidence. Schema drift blocks approval until a new template version is tested and activated.

## Worker identity and substitutions

Worker resolution is effective-dated and follows this order:

1. an explicit platform-worker mapping or accountant remap;
2. rider shift substitution covering the fact date;
3. Hunger/FTR disability/substitution covering the fact date;
4. working-ID history covering the fact date;
5. the current rider working ID as the final fallback.

The accountant may remap an unresolved or incorrect source worker ID before import approval. Overlapping mappings for the same platform worker ID are rejected. Every normalized fact retains its source sheet, row, and cell lineage.

