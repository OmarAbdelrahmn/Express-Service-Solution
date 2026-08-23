# Petrol System Operations

## Purpose

The petrol system imports daily vehicle fuel costs, resolves each Excel plate
to a vehicle, attributes the cost to the rider or riders who used that vehicle,
and provides daily and monthly cost reporting.

## Main entities

| Entity | Responsibility |
|---|---|
| `VehiclePetrolCost` | Raw daily imported cost: English plate, resolved `VehicleNumber`, cost, operational date, uploader, note, and resolution/attribution state. |
| `RiderPetrolCost` | A rider's share of one vehicle cost for a date; records vehicle, rider iqama, allocated cost, source, status record used, and notes. |
| `Vehicle` | The imported English plate is matched to `Vehicle.PlateNumberE`; its canonical `VehicleNumber` is then used by petrol records. |
| `RiderVehicleStatus` | Supplies permission windows and taken/returned history used to find the rider responsible for the cost. |

```text
Excel: PlateNumberE + Cost
       ↓
VehiclePetrolCost (one vehicle/date cost)
       ↓ attribution
RiderPetrolCost (one or more rider shares)
```

## Import and attribution flow

1. An administrator uploads an Excel file with `PlateNumberE` and `Cost` for a
   `reportDate`.
2. The system resolves the English plate to a vehicle and saves a
   `VehiclePetrolCost` record.
3. The attribution engine finds the rider assignment for that operational date:

   - first through an explicit vehicle permission window;
   - otherwise through the vehicle `Taken`/`Returned` status timeline;
   - otherwise creates an unattributed row for manual review.

4. One cost can create multiple rider-cost rows when a vehicle was switched
   between riders during the day.
5. Unresolved vehicle plates are retained with `HasResolutionError` rather
   than discarded. They can be resolved and attributed later.

## Attribution sources

| Source | Meaning |
|---|---|
| `Permission` | A `RiderVehicleStatus` permission period covers the report date. |
| `VehicleStatusTimeline` | The rider was determined from Taken/Returned events. |
| `Unattributed` | No rider could be resolved; manual action is needed. |
| `ManualOverride` | An administrator manually assigned the rider. |

## API summary

Base route: `/api/Petrol`

| Capability | Route | Role |
|---|---|---|
| Upload daily Excel | `POST /upload?reportDate=` | Master, Admin |
| Re-run all pending attribution | `POST /attribute-pending` | Master, Admin |
| Re-run one record | `POST /attribute/{id}` | Master, Admin |
| Manually assign rider | `PATCH /{vehicleNumber}/assign-rider?date=&riderIqamaNo=` | Master, Admin |
| Add a vehicle/date note | `PUT /{vehicleNumber}/note?date=&note=` | Master, Admin |
| View unresolved costs | `GET /unattributed?year=&month=` | Master, Admin |
| Daily full report | `GET /daily?date=` | Master, Admin, Member |
| Rider monthly/daily reports | `GET /rider/{iqamaNo}/monthly`, `/rider/{iqamaNo}/date` | Master, Admin, Member |
| Vehicle monthly/daily reports | `GET /vehicle/{vehicleNumber}/monthly`, `/vehicle/{vehicleNumber}/date` | Master, Admin, Member |
| Monthly rider/vehicle summaries | `GET /riders/summary`, `/vehicles/summary` | Master, Admin, Member |
| Rider company/housing report | `GET /riders/company-housing-report` | Master, Admin, Member |
| Correct latest permission start | `PATCH /rider/{iqamaNo}/shift-permission-start` | Master, Admin |

## Operational rules

- The upload date is the **operational report date**, not necessarily the file
  upload date.
- `VehiclePetrolCost` stores the original vehicle-level cost; `RiderPetrolCost`
  stores the allocation. These should be reviewed together when investigating
  a rider charge.
- A manually assigned row changes its source to `ManualOverride` and records
  the administrative context in its notes.
- Petrol reporting is linked to the vehicle take/return history. Accurate
  `RiderVehicleStatus` permission dates are essential for accurate cost
  allocation.
- `DELETE /api/Petrol/date/{date}` removes petrol data for a date. The current
  controller has its authorization attribute commented out, so this endpoint
  should be treated as sensitive until access control is confirmed.

## Source files

- `Domain/Entities/Petrol/VehiclePetrolCost.cs`
- `Domain/Entities/Petrol/RiderPetrolCost.cs`
- `Application/Service/Petrol/PetrolService.cs`
- `Express Service/Controllers/PetrolController.cs`
