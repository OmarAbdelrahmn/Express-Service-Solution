# Vehicle, Maintenance, and Spare-Parts Operations

## Scope

This guide documents the implemented vehicle lifecycle, vehicle maintenance,
spare-parts inventory, accessories, costs, and related API services.

## Main entities

| Entity | Purpose |
|---|---|
| `Vehicle` | Master vehicle record. `VehicleNumber` is its key; it also stores plates, serial, owner, manufacture details, licence expiry, images, and location. |
| `RiderDetails` | Holds the current rider-to-vehicle assignment through nullable `VehicleNumber`. |
| `RiderVehicleStatus` | Full vehicle operational history: rider, vehicle, status, reason, permission dates, timestamp, and `IsActive`. |
| `TempVehicleOperation` | Take, return, problem, and switch requests that await admin approval. |
| `SparePart` | Part inventory by quantity, price, and location. |
| `SparePartUsage` | A spare part consumed by a specific vehicle; includes quantity, cost, use date, and source location. |
| `RiderAccessory` / `RiderAccessoryUsage` | Accessory stock and items issued to riders. |
| `MaintenanceInterval` | Recurring maintenance rule for a spare part or accessory. |
| `VehiclePetrolCost` / `RiderPetrolCost` | Daily vehicle petrol spend and its rider-level attribution. |
| `Supplier`, `Bill`, `Transfer`, `Return` | Supplier, purchase receipt, stock movement, and vendor return documents. |

## Relationships

```text
Vehicle
 ├─ RiderDetails.VehicleNumber       current assignment
 ├─ RiderVehicleStatus               assignment/condition history
 ├─ SparePartUsage                   maintenance work and part cost
 └─ VehiclePetrolCost → RiderPetrolCost

Supplier → Bill → inventory → Transfer → housing inventory → Usage
                                         └───────────────→ Return
```

## Vehicle statuses

| Status | Operational meaning | Availability effect |
|---|---|---|
| `Taken` | Assigned to a rider. | Unavailable. |
| `Returned` | Vehicle was returned; historical event. | Available unless another active blocking status exists. |
| `Problem` | Fault was reported. | Unavailable; assignment is ended. |
| `Stolen` | Stolen report is active. | Unavailable; assignment is ended. |
| `BreakUp` | Vehicle is marked broken up. | Unavailable; assignment is ended. |
| `OutOfService` | Removed from operation. | Unavailable; assignment is ended. |
| `fixProblem` / `switched` | Event/request markers in the enum. | The implemented fix and switch flows create the appropriate taken/returned history. |

Availability is derived from active `RiderVehicleStatus` rows. A vehicle is
unavailable when it has an active `Taken`, `Problem`, `Stolen`, `BreakUp`, or
`OutOfService` status.

## Vehicle lifecycle

### Take

Admin route: `POST /api/vehicles/take?IqamaNo=&vehicleNumber=&reason=&permission=`.

The service validates an enabled rider, no current rider assignment, an
existing available vehicle, then sets `RiderDetails.VehicleNumber`, updates
the vehicle location to the rider housing where available, and creates active
`Taken` history with permission dates.

### Return

Admin route: `POST /api/vehicles/return?IqamaNo=&vehicleNumber=&reason=`.

The service verifies the rider holds the vehicle, closes the active `Taken`
permission, clears the current assignment, and adds a `Returned` history row.

### Switch

Admin route: `POST /api/vehicles/switch?IqamaNo=&newVehiclePlate=&reason=&permission=`.

The service runs a transaction: it returns the old vehicle, validates the new
vehicle is available, assigns the new vehicle, updates its location, and adds
the new active `Taken` status.

### Requests and approval

Members submit requests through `/api/temp`:

| Request | Submit | Admin action |
|---|---|---|
| Take | `POST /vehicle-request-take` | `PUT /vehicle-resolve` |
| Return | `POST /vehicle-request-return` | `PUT /vehicle-resolve` |
| Report problem | `POST /vehicle-request-problem` | `PUT /vehicle-resolve` |
| Pending list | `GET /vehicles` | Master/Admin |

Housing managers can also create housing-scoped requests through `/api/member`,
including `POST /vehicles/request-switch-vehicel`.

## Vehicle condition routes

Base route: `/api/vehicles`

| Action | Route |
|---|---|
| Report problem / fix | `POST /report-problem`, `POST /fix-problem` |
| Report / recover stolen | `POST /stolen`, `PUT /recover-stolen` |
| Mark break-up | `POST /break-up` |
| Mark / restore out of service | `POST /out-of-service`, `PUT /restore-out-of-service` |
| Status views | `GET /available`, `/taken`, `/problem`, `/stolen`, `/breakup`, `/out-of-service`, `/group-by-status` |
| Assignment/history | `GET /with-riders`, `/with-rider/{plate}`, `/vehicle-history/{plate}`, `/rider-history/{iqamaNo}` |

## Location and permissions

Vehicle `Location` normally holds a housing name or `الشركة` for company/
unassigned vehicles. It can be changed directly, set when a vehicle is taken
or switched, synchronised in bulk, or updated through the relocation import.

Active assignments store permission start and end dates. The daily Hangfire
job `vehicle-permission-renewal` renews active permissions that end today.

## Spare-parts maintenance

### Inventory flow

1. Create a supplier and receive a `Bill`; bill items increase part/accessory stock.
2. Transfer stock from company inventory to a housing with `Transfer` records.
3. Record `SparePartUsage` against a vehicle; quantity is deducted and cost is saved.
4. Issue `RiderAccessoryUsage` to a rider; accessory quantity is deducted.
5. Use `Return` records to send inventory to a supplier and decrease stock.
6. Configure intervals so recorded usage/issuance becomes the maintenance history.

Vehicle spare-part use does **not** automatically fix a vehicle `Problem`
status. Fixing the operational status remains a separate vehicle action.

### Spare-part API

Base route: `/api/SparePart`

- Stock CRUD/search: `GET`, `GET /{id}`, `POST`, `PUT /{id}`, `DELETE /{id}`, `GET /search?q=`.
- Vehicle usage: `POST /{id}/usage`, batch `POST /spare-parts`.
- Usage correction: `PUT /usage/{usageId}`, `DELETE /usage/{usageId}`.
- History: `GET /{id}/history`, `GET /vehicle/{vehicleNumber}/history`.
- Housing/company stock reporting: `/company-stock`, `/all-housings`, `/all-housings/details`, `/comparison`.
- Spreadsheet stock checking/sync: `/check`, `/sync/{housingId}`, `/sync-company-stock`, `/sync-price`.

Updating or deleting a usage restores the previous stock quantity before the
new effect is applied, keeping inventory aligned with vehicle maintenance data.

### Accessories, purchase, and movement

| Area | Base API route | Main function |
|---|---|---|
| Rider accessories | `/api/RiderAccessory` | Manage stock, issue to rider, record batches, inspect history. |
| Suppliers | `/api/Supplier` | Manage active suppliers. |
| Bills | `/api/Bill` | Receive stock from suppliers. |
| Transfers | `/api/Transfer` | Move items between company and housing locations. |
| Returns | `/api/Return` | Return parts/accessories to suppliers. |
| Item movement report | `/api/ItemMovementReport` | Transfers, usages, and current stock snapshots. |

## Maintenance reminders

Base route: `/api/maintenance` (Master/Admin).

An interval applies to one spare part or one accessory, may be location-scoped,
and contains `IntervalDays` plus `AlertDaysBeforeDue`.

```text
next due date = last usage or issue date + interval days
```

Statuses are `OK`, `Upcoming`, `DueToday`, `Overdue`, and `NeverDone`.
The reminder dashboard returns due vehicle items from `SparePartUsage` and due
rider items from `RiderAccessoryUsage`.

| Operation | Route |
|---|---|
| Manage interval rules | `GET/POST /intervals`, `GET/PUT/DELETE /intervals/{id}` |
| Toggle interval | `PATCH /intervals/{id}/toggle` |
| Due dashboard | `GET /reminders?checkDate=` |

## Costs and petrol

`/api/CostTracking` provides vehicle spare-part cost, rider accessory cost,
period summaries, and rider allocation of vehicle part costs using vehicle
permission windows.

`/api/Petrol` imports daily petrol Excel data by English plate, resolves it to
a vehicle, and attributes cost to riders using permission windows or vehicle
status history. Unmatched records are retained for manual resolution. Daily,
monthly, vehicle, rider, summary, and unattributed-cost reports are available.

## Important identifier note

`VehicleNumber` is the canonical key, but several vehicle endpoints use
`PlateNumberA` while naming their parameter `vehicleNumber`. Confirm whether a
route expects the Arabic plate or canonical vehicle number before integrating.

## Source files

- `Express Service/Controllers/VehicleController.cs`
- `Application/Service/Empolyee/VehicleService.cs`
- `Domain/Entities/Vehicle.cs` and `Domain/Entities/RiderVehicleStatus.cs`
- `Express Service/Controllers/SparePartController.cs`
- `Application/Service/SparePart/SparePartService.cs`
- `Express Service/Controllers/MaintenanceReminderController.cs`
- `Application/Service/SparePart/Reminderservice.cs`
- `Express Service/Controllers/PetrolController.cs`
