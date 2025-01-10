using System;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;

public sealed class TranscriptText
{
    [VectorStoreRecordKey]
    public Guid Id { get; set; }

    [VectorStoreRecordData(IsFullTextSearchable = true)]
    public string Text { get; set; } = string.Empty;

    [VectorStoreRecordData(IsFilterable = true)]
    public double StartTime { get; set; }

    [VectorStoreRecordData(IsFilterable = true)]
    public double EndTime { get; set; }

    [VectorStoreRecordData(IsFilterable = true)]
    public DateTime EpisodeDate { get; set; }

    [VectorStoreRecordData(IsFilterable = true)]
    public string EpisodeNumber { get; set; } = string.Empty;

    [VectorStoreRecordData(IsFilterable = true)]
    public string EpisodeTitle { get; set; } = string.Empty;

    [VectorStoreRecordData(IsFullTextSearchable = true)]
    public string ChunkTopic { get; set; } = string.Empty;

    [VectorStoreRecordData(IsFullTextSearchable = true)]
    public string Topics { get; set; } = string.Empty;


    [VectorStoreRecordVector(1536)]
    public ReadOnlyMemory<float> Embedding { get; init; }

}