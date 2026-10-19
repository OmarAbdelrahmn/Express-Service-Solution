-- Petrol diagnosis, 18 October 2026. SELECT only; no repair statements.
-- Run against the intended database. Results can change as uploads continue.

-- Identifier mismatch in the assignment import: chassis and Arabic plate are different.
SELECT COUNT(*) AS CurrentAssignments,
       SUM(CASE WHEN LOWER(REPLACE(v.VehicleNumber, ' ', '')) <>
                          LOWER(REPLACE(v.PlateNumberA, ' ', '')) THEN 1 ELSE 0 END)
           AS DifferentChassisAndArabicPlate
FROM RiderDetails d
JOIN Vehicles v ON v.VehicleNumber = d.VehicleNumber;

SELECT CONVERT(date, Timestamp) AS EventDate, COUNT(*) AS AutoReturns
FROM RiderVehicleStatus
WHERE StatusType = 2 AND Reason LIKE 'Auto-returned by system sync%'
GROUP BY CONVERT(date, Timestamp)
ORDER BY EventDate;

-- Confirm the return/take ordering and backdated permission in known examples.
SELECT v.PlateNumberE, s.Id, s.StatusType, s.IsActive, s.Timestamp,
       s.PermissionStartDate, s.PermissionEndDate, s.Reason
FROM RiderVehicleStatus s
JOIN Vehicles v ON v.VehicleNumber = s.VehicleNumber
WHERE v.PlateNumberE IN ('BE7298', 'BE7284', 'AD6017')
ORDER BY v.PlateNumberE, s.Timestamp, s.Id;

-- Raw costs versus all saved allocations, including unassigned allocations.
SELECT COUNT(*) AS SourceRows, SUM(Cost) AS SourceTotal FROM VehiclePetrolCosts;
SELECT COUNT(*) AS AllocationRows, SUM(Cost) AS AllocationTotal,
       SUM(CASE WHEN RiderIqamaNo IS NULL THEN Cost ELSE 0 END) AS UnassignedTotal
FROM RiderPetrolCosts;

SELECT CONVERT(char(7), v.Date, 126) AS Month, COUNT(*) AS SourceRows,
       SUM(v.Cost) AS SourceTotal, SUM(COALESCE(a.Total, 0)) AS AllocationTotal,
       SUM(CASE WHEN a.AllocationRows = 0 THEN 1 ELSE 0 END) AS MissingAllocations
FROM VehiclePetrolCosts v
OUTER APPLY (
    SELECT COUNT(*) AS AllocationRows, SUM(r.Cost) AS Total
    FROM RiderPetrolCosts r WHERE r.VehiclePetrolCostId = v.Id
) a
GROUP BY CONVERT(char(7), v.Date, 126)
ORDER BY Month;

-- Multiple source rows require workbook review, not automatic deletion.
SELECT VehicleNumber, Date, COUNT(*) AS SourceRows, SUM(Cost) AS TotalCost
FROM VehiclePetrolCosts WHERE VehicleNumber IS NOT NULL
GROUP BY VehicleNumber, Date HAVING COUNT(*) > 1
ORDER BY Date DESC, VehicleNumber;

-- This is a symptom count: a current assignment alone does not establish past possession.
SELECT CONVERT(char(7), r.Date, 126) AS Month, COUNT(*) AS UnassignedWithCurrentRider
FROM RiderPetrolCosts r
WHERE r.RiderIqamaNo IS NULL AND EXISTS (
    SELECT 1 FROM RiderDetails d WHERE d.VehicleNumber = r.VehicleNumber
)
GROUP BY CONVERT(char(7), r.Date, 126)
ORDER BY Month;
