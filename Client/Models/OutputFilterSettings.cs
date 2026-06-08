﻿// Хранит настройки выборки данных перед запуском OutputPipeline.
public class OutputFilterSettings
{
    public bool LatestOnly { get; set; } = true;
    public bool OnlyChanged { get; set; } = false;

    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string SaleRegion { get; set; } = "";

    public int? YearFrom { get; set; } = null;
    public int? YearTo { get; set; } = null;
}
