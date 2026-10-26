# Rider score performance imports

`POST /api/RiderScorePerformance/import?performanceDate=YYYY-MM-DD` accepts a
month-to-date cumulative report. The date must match the report's ending date.
Saved counters represent the difference from earlier saved totals in that month.
If reports are weeks apart, the new record includes that interval's difference.

Failed orders can decrease after the platform accepts an appeal. For example,
a cumulative total changing from 3 to 2 saves a `FailedOrdersByRider` adjustment
of -1. Monthly sums then equal the latest platform total of 2. API consumers
should display negative daily failed-order values as corrections. Do not clamp
these adjustments to zero or replace them with the uploaded cumulative value.
The cumulative failed-order value in the workbook must still be non-negative.
Manual create/update endpoints also accept signed daily failed-order adjustments.

The other five counters must not decrease. A rejected row identifies each
conflicting column, its uploaded value, its saved baseline, and the report date.

When inserting a report before an existing report in the same month, the service
adjusts the next record's differences so later cumulative totals stay unchanged.
It checks the non-decreasing counters against both earlier totals and the next
report. Failed-order corrections may be negative on either record. Inserts and
adjustments are saved together; SQL Server imports use a serializable transaction
to protect the baseline while importing. Existing rider/date records are skipped.

This prevents future backfill double-counting. It does not automatically repair
records already affected by earlier imports; reconcile those against the original
reports before changing historical data.
