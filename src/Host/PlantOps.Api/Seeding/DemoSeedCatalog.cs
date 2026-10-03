namespace PlantOps.Api.Seeding;

// The hand-written part of the demo data: what a Penang SMT / final-assembly plant would actually have. Manufacturers
// and models are real product families; serial numbers, bins and texts are invented. Everything random (which asset
// breaks, when) lives in DemoSeedGenerator.
internal static class DemoSeedCatalog
{
    public static readonly SeedUser[] Users =
    [
        new(DemoSeedIds.Olivia, "Olivia Tan", "olivia.tan@plantops.local", "operator"),
        new(DemoSeedIds.Tom, "Tom Lim", "tom.lim@plantops.local", "technician"),
        new(DemoSeedIds.Sam, "Sam Wong", "sam.wong@plantops.local", "supervisor"),
        new(DemoSeedIds.Ada, "Ada Rahman", "ada.rahman@plantops.local", "admin"),
    ];

    internal sealed record AssetRow(
        string Tag,
        string Name,
        string Manufacturer,
        string Model,
        string LineCode,
        string Station,
        string Criticality,
        int YearsInService);

    public static readonly AssetRow[] Assets =
    [
        // SMT line 1 (the showcase line: placers, reflow and inspection).
        new("SMT1-LDR-01", "Magazine board loader", "Nutek", "NTL-250", "SMT-1", "Loader", "C", 6),
        new("SMT1-PRN-01", "DEK Horizon stencil printer", "ASMPT DEK", "Horizon 03iX", "SMT-1", "Printer", "A", 6),
        new("SMT1-SPI-01", "Koh Young solder paste inspection", "Koh Young", "aSPIre3", "SMT-1", "SPI", "B", 5),
        new("SMT1-PNP-01", "Fuji NXT III pick-and-place", "Fuji", "NXT III", "SMT-1", "Placer 1", "A", 6),
        new("SMT1-PNP-02", "Fuji NXT III pick-and-place", "Fuji", "NXT III", "SMT-1", "Placer 2", "A", 6),
        new("SMT1-PNP-03", "Fuji AIMEX IIIc multi-function placer", "Fuji", "AIMEX IIIc", "SMT-1", "Placer 3", "B", 3),
        new("SMT1-RFL-01", "Heller 1913 MK5 reflow oven", "Heller Industries", "1913 MK5", "SMT-1", "Reflow", "A", 6),
        new("SMT1-AOI-01", "Koh Young Zenith AOI", "Koh Young", "Zenith 2", "SMT-1", "AOI", "B", 4),
        new("SMT1-CNV-01", "Inline buffer conveyor", "Nutek", "NTC-120B", "SMT-1", "Buffer", "C", 6),
        new("SMT1-ULD-01", "Magazine board unloader", "Nutek", "NTU-250", "SMT-1", "Unloader", "C", 6),
        new("SMT1-MSD-01", "Moisture-sensitive device dry cabinet", "Totech", "SDB-1002", "SMT-1", "Component store", "C", 5),

        // SMT line 2.
        new("SMT2-PRN-01", "DEK Horizon stencil printer", "ASMPT DEK", "Horizon 03iX", "SMT-2", "Printer", "A", 3),
        new("SMT2-SPI-01", "Koh Young solder paste inspection", "Koh Young", "aSPIre3", "SMT-2", "SPI", "B", 3),
        new("SMT2-PNP-01", "Fuji NXT III pick-and-place", "Fuji", "NXT III", "SMT-2", "Placer 1", "A", 3),
        new("SMT2-PNP-02", "Yamaha YSM20R high-speed mounter", "Yamaha", "YSM20R", "SMT-2", "Placer 2", "A", 2),
        new("SMT2-RFL-01", "Heller 1809 MK5 reflow oven", "Heller Industries", "1809 MK5", "SMT-2", "Reflow", "A", 3),
        new("SMT2-AOI-01", "Omron VT-S730 AOI", "Omron", "VT-S730", "SMT-2", "AOI", "B", 3),
        new("SMT2-CNV-01", "Inline buffer conveyor", "Nutek", "NTC-120B", "SMT-2", "Buffer", "C", 3),

        // Final assembly.
        new("FA1-SCR-01", "Screwdriving cell 1", "Atlas Copco", "Tensor ST", "FA-1", "Station 10", "B", 4),
        new("FA1-SCR-02", "Screwdriving cell 2", "Atlas Copco", "Tensor ST", "FA-1", "Station 20", "B", 4),
        new("FA1-SCR-03", "Screwdriving cell 3", "Atlas Copco", "Tensor ST", "FA-1", "Station 30", "B", 4),
        new("FA1-SCR-04", "Screwdriving cell 4", "Atlas Copco", "Tensor ST", "FA-1", "Station 40", "B", 2),
        new("FA1-CNV-01", "Final assembly line conveyor", "Bosch Rexroth", "TS 2plus", "FA-1", "Line conveyor", "B", 5),
        new("FA1-DSP-01", "Adhesive dispensing cell", "Nordson EFD", "Ultimus V", "FA-1", "Station 50", "C", 4),
        new("FA1-LSR-01", "Laser marker", "Keyence", "MD-X1520", "FA-1", "Station 60", "C", 3),

        // Functional test.
        new("TEST1-ICT-01", "In-circuit tester", "Keysight", "i3070 Series 5i", "TEST-1", "ICT 1", "A", 5),
        new("TEST1-ICT-02", "In-circuit tester", "Keysight", "i3070 Series 5i", "TEST-1", "ICT 2", "A", 5),
        new("TEST1-ICT-03", "In-circuit tester", "Teradyne", "TestStation LH", "TEST-1", "ICT 3", "B", 2),
        new("TEST1-FCT-01", "Functional tester", "Keysight", "E6198B switch/load unit", "TEST-1", "FCT 1", "B", 4),
        new("TEST1-FCT-02", "Functional tester", "National Instruments", "PXIe-1085 test rack", "TEST-1", "FCT 2", "B", 3),
        new("TEST1-BRN-01", "Burn-in chamber", "Espec", "PU-3KP", "TEST-1", "Burn-in", "C", 5),
    ];

    /// <param name="Low">True for parts the demo shows as low on stock (available at or below the reorder level).</param>
    internal sealed record PartRow(string PartNumber, string Name, string Unit, string Bin, int ReorderLevel, bool Low = false);

    public static readonly PartRow[] Parts =
    [
        new("NZL-CN040", "CN040 nozzle", "pcs", "A1-01", 15),
        new("NZL-CN065", "CN065 nozzle", "pcs", "A1-02", 15),
        new("NZL-CN140", "CN140 nozzle", "pcs", "A1-03", 10),
        new("NZL-CN220", "CN220 nozzle", "pcs", "A1-04", 10),
        new("FDR-8MM-001", "8 mm tape feeder", "pcs", "A2-01", 6),
        new("FDR-12MM-001", "12 mm tape feeder", "pcs", "A2-02", 4),
        new("FDR-16MM-001", "16 mm tape feeder", "pcs", "A2-03", 3, Low: true),
        new("FLT-PNP-VAC", "Placement head vacuum filter", "pcs", "A3-01", 20),
        new("FLT-RFL-FLX", "Reflow flux filter cartridge", "pcs", "A3-02", 6),
        new("FLT-AIR-5U", "Inline compressed-air filter, 5 micron", "pcs", "A3-03", 8),
        new("HTR-RFL-ZN-01", "Reflow zone heater element", "pcs", "B1-01", 2, Low: true),
        new("FAN-RFL-120", "Reflow convection fan, 120 mm", "pcs", "B1-02", 3),
        new("FAN-CAB-80", "Cabinet cooling fan, 80 mm", "pcs", "B1-03", 6),
        new("BLT-CNV-300", "PCB conveyor belt, 300 mm", "pcs", "B2-01", 4),
        new("MTR-CNV-24V", "Conveyor drive motor, 24 V", "pcs", "B2-02", 2),
        new("BRG-LM-8", "Linear bearing LM8UU", "pcs", "B2-03", 10, Low: true),
        new("SNS-PROX-M12", "Proximity sensor M12", "pcs", "C1-01", 6),
        new("SNS-OPT-FIB", "Fibre-optic board sensor", "pcs", "C1-02", 4),
        new("SQG-DEK-250", "Printer squeegee blade, 250 mm", "pcs", "D1-01", 6),
        new("STN-WIPE-ROLL", "Stencil wiper roll", "roll", "D1-02", 12),
        new("LMP-AOI-RGB", "AOI RGB ring light", "pcs", "D2-01", 2),
        new("CBL-FFC-20P", "Flat flex cable, 20-pin", "pcs", "D2-02", 8),
        new("BIT-TQ-PH2", "Screwdriver bit PH2", "pcs", "E1-01", 30),
        new("BIT-TQ-T10", "Screwdriver bit Torx T10", "pcs", "E1-02", 30),
        new("PRB-ICT-GP", "ICT spring probe, 100 mil", "pcs", "E2-01", 100),
    ];

    internal sealed record PmRow(
        string AssetTag,
        string Title,
        string Instructions,
        int IntervalDays,
        int LeadDays,
        string Priority,
        string? PartNumber,
        int PartQuantity);

    // Intervals as a plant would set them: weekly-to-quarterly care for wear parts, longer for calibration.
    public static readonly PmRow[] PmSchedules =
    [
        new("SMT1-RFL-01", "Reflow oven: clean flux residue, lubricate chain, verify zone profile",
            "1. Cool the oven below 60 C. 2. Vacuum flux residue from zones 1-8 and the exhaust. 3. Lubricate the conveyor chain with high-temperature grease. 4. Replace the flux filter cartridge. 5. Run the profiler board and file the profile.",
            30, 3, "P3", "FLT-RFL-FLX", 1),
        new("SMT2-RFL-01", "Reflow oven: clean flux residue, lubricate chain, verify zone profile",
            "1. Cool the oven below 60 C. 2. Vacuum flux residue from all zones and the exhaust. 3. Lubricate the conveyor chain. 4. Replace the flux filter cartridge. 5. Run the profiler board and file the profile.",
            30, 3, "P3", "FLT-RFL-FLX", 1),
        new("SMT1-PNP-01", "Placement heads: clean nozzles, replace vacuum filters",
            "1. Remove all nozzles and clean them in the ultrasonic bath. 2. Replace the head vacuum filters. 3. Check the nozzle changer. 4. Run the pick-and-place self-test.",
            14, 2, "P3", "FLT-PNP-VAC", 2),
        new("SMT1-PRN-01", "Stencil printer: replace squeegee blades, check wiper vacuum",
            "1. Replace both squeegee blades. 2. Clean the vacuum plenum under the wiper. 3. Verify print pressure and snap-off with a test print.",
            45, 5, "P3", "SQG-DEK-250", 2),
        new("SMT1-AOI-01", "AOI: camera and lighting calibration",
            "1. Clean the lens and light ring. 2. Run the colour and height calibration with the reference board. 3. Re-run the gauge study if the offsets changed.",
            60, 5, "P4", null, 0),
        new("FA1-SCR-01", "Screwdriving cell: torque verification and bit replacement",
            "1. Verify torque with the transducer at three set points. 2. Replace the PH2 bit and the bit holder spring. 3. Clean the screw feeder track.",
            90, 7, "P4", "BIT-TQ-PH2", 4),
        new("TEST1-ICT-01", "ICT: probe inspection and fixture vacuum seal check",
            "1. Inspect the fixture probes under magnification and replace worn ones. 2. Check the vacuum seal and the gasket. 3. Run the golden-board test.",
            90, 7, "P3", "PRB-ICT-GP", 10),
        new("SMT1-CNV-01", "Conveyor: belt tension, bearing grease, sensor clean",
            "1. Check belt tension and tracking. 2. Grease the bearings. 3. Clean the board sensors and test the stop and release.",
            120, 7, "P4", null, 0),
    ];

    /// <summary>One typical fault of a kind of machine. The part (if any) is what the repair consumes.</summary>
    internal sealed record IssueTemplate(
        string Title,
        string Description,
        string Resolution,
        bool CanStopTheLine,
        string? PartNumber,
        int MinQuantity,
        int MaxQuantity);

    // Keyed by the kind segment of the tag (SMT1-PNP-01 is a "PNP"). Loader, unloader and conveyor share one list.
    public static readonly Dictionary<string, IssueTemplate[]> Issues = BuildIssues();

    public static string KindOf(string tag) => tag.Split('-')[1] is "LDR" or "ULD" ? "CNV" : tag.Split('-')[1];

    public static readonly string[] RejectionReasons =
    [
        "Duplicate of a work order that is already open for this machine.",
        "Cosmetic issue only; fold it into the next planned maintenance.",
        "No fault found when checked at the machine; ask the operator to re-raise if it returns.",
    ];

    public static readonly string[] CancellationReasons =
    [
        "The fault cleared after a restart and has not returned; no repair needed.",
        "The machine was taken offline for a planned line changeover; the job is no longer needed.",
        "Raised against the wrong machine; a new work order was submitted.",
    ];

    private static Dictionary<string, IssueTemplate[]> BuildIssues()
    {
        var cnv = new IssueTemplate[]
        {
            new("Belt tracking off, boards catching the rails",
                "Boards drift to one side and catch on the rail entry. Intermittent stops at this machine.",
                "Re-tensioned and re-aligned the belt, replaced it because of edge wear, and ran 100 boards through.",
                true, "BLT-CNV-300", 1, 1),
            new("Board sensor not detecting boards",
                "The stop sensor misses boards at speed, so the next machine starves or double-feeds.",
                "Cleaned and re-aligned the sensor; replaced the proximity sensor that was failing intermittently.",
                false, "SNS-PROX-M12", 1, 1),
            new("Drive motor running hot",
                "Motor housing too hot to touch after an hour; speed drops under load.",
                "Replaced the drive motor and checked the belt load; temperature settled in the normal band.",
                false, "MTR-CNV-24V", 1, 1),
            new("Magazine not indexing",
                "The magazine lift skips a slot every few cycles.",
                "Re-set the indexer reference and replaced two worn linear bearings; ran 20 magazines without a skip.",
                true, "BRG-LM-8", 2, 2),
        };

        return new Dictionary<string, IssueTemplate[]>
        {
            ["PNP"] =
            [
                new("Nozzle clogged, rising pick errors on head 3",
                    "Pick error rate climbed above 0.5 percent on head 3 over the last two shifts. Suspect a clogged nozzle.",
                    "Ultrasonic-cleaned the nozzles and replaced the worn ones; pick errors back under 0.05 percent.",
                    false, "NZL-CN040", 2, 6),
                new("Feeder not advancing 8 mm tape",
                    "The feeder in slot 14 stops advancing and raises a component-out alarm although the reel is full.",
                    "Replaced the feeder and re-calibrated the pitch; verified with 50 pick cycles.",
                    false, "FDR-8MM-001", 1, 1),
                new("Low vacuum alarm on placement head",
                    "Placement head 1 raises a low-vacuum alarm intermittently and the line stops until it is reset.",
                    "Replaced the clogged vacuum filter and checked the tubing for leaks; vacuum stable at setpoint.",
                    true, "FLT-PNP-VAC", 1, 2),
                new("Vision camera calibration drift",
                    "Placement offsets are trending 40 microns on X after the last changeover.",
                    "Re-ran camera and head calibration with the glass jig; offsets within tolerance.",
                    false, null, 0, 0),
            ],
            ["RFL"] =
            [
                new("Zone 4 heater does not reach setpoint",
                    "Zone 4 stays 12 C under setpoint and the profile is out of window; boards are being held.",
                    "Replaced the failed zone 4 heater element and verified the profile with the profiler board.",
                    true, "HTR-RFL-ZN-01", 1, 1),
                new("Convection fan vibration and noise",
                    "A zone 6 fan is noisy and the oven shows a vibration warning.",
                    "Replaced the fan assembly, balanced it and rechecked the airflow.",
                    false, "FAN-RFL-120", 1, 1),
                new("Chain conveyor jams at the oven exit",
                    "The chain binds at the exit and boards pile up behind it.",
                    "Cleaned the flux build-up, lubricated the chain with high-temperature grease and re-aligned the rails.",
                    true, null, 0, 0),
                new("Exhaust flow alarm",
                    "The exhaust flow alarm triggers after the night shift; flux filter looks saturated.",
                    "Replaced the saturated flux filter cartridge and cleaned the exhaust duct.",
                    false, "FLT-RFL-FLX", 1, 2),
            ],
            ["AOI"] =
            [
                new("False-call rate above threshold",
                    "False calls doubled over two days and the repair station is backing up.",
                    "Cleaned the lens and re-tuned the inspection library thresholds; false calls back to normal.",
                    false, null, 0, 0),
                new("Lighting ring dim on the side camera",
                    "Side camera images are dark and defect detection is unreliable on dark solder joints.",
                    "Replaced the RGB ring light and re-ran the light calibration.",
                    false, "LMP-AOI-RGB", 1, 1),
            ],
            ["SPI"] =
            [
                new("Height measurement offset on the calibration plate",
                    "The daily calibration plate check reads 8 microns high and fails.",
                    "Re-calibrated against the reference plate; the offset is gone.",
                    false, null, 0, 0),
                new("Laser head communication error",
                    "The inspection head drops its connection and the machine halts the line.",
                    "Re-seated and replaced the flat flex cable to the head; error cleared and a 4-hour soak passed.",
                    true, "CBL-FFC-20P", 1, 1),
            ],
            ["PRN"] =
            [
                new("Stencil wiper not advancing",
                    "The under-stencil wiper does not advance and paste smears on the underside.",
                    "Replaced the wiper roll and cleared the paper path; wiper cycle verified.",
                    false, "STN-WIPE-ROLL", 1, 1),
                new("Poor paste print definition",
                    "SPI shows insufficient paste on fine-pitch pads after the last stencil change.",
                    "Replaced the worn squeegee blades and re-set print pressure; SPI results back in the window.",
                    false, "SQG-DEK-250", 2, 2),
                new("Rail width fault on the printer",
                    "The printer cannot reach the programmed rail width and rejects the job.",
                    "Cleaned and re-lubricated the width screw, then re-homed the axis.",
                    true, null, 0, 0),
            ],
            ["SCR"] =
            [
                new("Torque error E-12 at the station",
                    "The tool reports torque error E-12 on roughly one in twenty screws.",
                    "Replaced the worn bit and verified torque with the transducer.",
                    false, "BIT-TQ-PH2", 2, 4),
                new("Screw feeder jam",
                    "Screws jam in the feeder track and the cell waits for an operator.",
                    "Cleared the feeder track and adjusted the escapement; 200 screws without a jam.",
                    false, null, 0, 0),
                new("Cam-out on Torx screws",
                    "Screw heads are damaged on a Torx product variant.",
                    "Replaced the T10 bits and checked the bit holder run-out.",
                    false, "BIT-TQ-T10", 2, 4),
            ],
            ["ICT"] =
            [
                new("Fixture vacuum leak, test coverage dropping",
                    "The fixture does not pull full vacuum; contact failures on the second net group.",
                    "Replaced the fixture seal and a batch of worn spring probes; vacuum holds at spec.",
                    true, "PRB-ICT-GP", 8, 16),
                new("Probe contact failures on one net group",
                    "Repeating open-circuit failures on the same nets point to bad probes.",
                    "Replaced the worn probes and re-ran the golden board.",
                    false, "PRB-ICT-GP", 4, 10),
            ],
            ["FCT"] =
            [
                new("Fixture communication timeout",
                    "The tester loses the DUT link every 30 to 40 boards.",
                    "Replaced the USB hub and re-seated the cabling; a 500-cycle soak passed.",
                    false, null, 0, 0),
                new("Relay card failure",
                    "Self-test fails on the relay card and the stand cannot run the full test plan.",
                    "Swapped the relay card and re-ran the self-test.",
                    true, null, 0, 0),
            ],
            ["BRN"] =
            [
                new("Chamber temperature overshoot",
                    "The chamber overshoots its setpoint by 6 C at ramp-up and trips the safety limit.",
                    "Re-tuned the control loop and replaced the circulation fan.",
                    false, "FAN-CAB-80", 1, 1),
            ],
            ["MSD"] =
            [
                new("Dry cabinet humidity above 10 percent RH",
                    "The cabinet display reads 14 percent RH and the floor-life log is out of date.",
                    "Replaced the door gasket and ran a desiccant regeneration cycle; humidity back under 5 percent.",
                    false, null, 0, 0),
            ],
            ["DSP"] =
            [
                new("Dispensing volume drifting",
                    "Bead size varies between shots and glue squeezes out on some units.",
                    "Replaced the needle and re-calibrated the shot size.",
                    false, null, 0, 0),
                new("Air supply pressure drop at the cell",
                    "The dispenser reports low air pressure when the neighbouring cell runs.",
                    "Replaced the inline air filter; pressure stable under load.",
                    false, "FLT-AIR-5U", 1, 1),
            ],
            ["LSR"] =
            [
                new("Mark contrast low on the housing",
                    "The laser mark is faint and the scanner rejects some data matrix codes.",
                    "Cleaned the lens and re-focused the laser; contrast restored.",
                    false, null, 0, 0),
            ],
            ["CNV"] = cnv,
        };
    }
}
