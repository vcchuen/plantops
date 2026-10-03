using PlantOps.Perf;

namespace PlantOps.Perf.Tests;

public class StatisticsMessagesTests
{
    [Fact]
    public void Logical_reads_are_summed_over_tables_and_lob_reads_are_ignored()
    {
        string[] messages =
        [
            "Table 'WorkOrderFacts'. Scan count 1, logical reads 147, physical reads 0, page server reads 0, read-ahead reads 0, page server read-ahead reads 0, lob logical reads 5, lob physical reads 0, lob page server reads 0, lob read-ahead reads 0, lob page server read-ahead reads 0.",
            "Table 'Worktable'. Scan count 0, logical reads 3, physical reads 0, page server reads 0, read-ahead reads 0, page server read-ahead reads 0, lob logical reads 0, lob physical reads 0, lob page server reads 0, lob read-ahead reads 0, lob page server read-ahead reads 0.",
        ];

        Assert.Equal(150, StatisticsMessages.LogicalReads(messages));
    }

    [Fact]
    public void No_io_messages_means_zero_reads()
    {
        Assert.Equal(0, StatisticsMessages.LogicalReads([]));
    }

    [Fact]
    public void Execution_time_ignores_the_parse_and_compile_block()
    {
        string[] messages =
        [
            "SQL Server parse and compile time: \n   CPU time = 12 ms, elapsed time = 40 ms.",
            "\n SQL Server Execution Times:\n   CPU time = 31 ms,  elapsed time = 57 ms.",
        ];

        var time = StatisticsMessages.ExecutionTime(messages);

        Assert.Equal(new SqlTiming(31, 57), time);
    }

    [Fact]
    public void Execution_time_takes_the_last_block()
    {
        string[] messages =
        [
            "SQL Server Execution Times:\n   CPU time = 1 ms,  elapsed time = 2 ms.",
            "SQL Server Execution Times:\n   CPU time = 3 ms,  elapsed time = 4 ms.",
        ];

        Assert.Equal(new SqlTiming(3, 4), StatisticsMessages.ExecutionTime(messages));
    }

    [Fact]
    public void Execution_time_is_null_without_an_execution_block()
    {
        Assert.Null(StatisticsMessages.ExecutionTime(["SQL Server parse and compile time: \n   CPU time = 0 ms, elapsed time = 0 ms."]));
    }
}

public class PlanXmlTests
{
    // Trimmed from the shape SQL Server returns: a Stream Aggregate over a clustered index scan, in the showplan namespace.
    private const string ScanPlan = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan" Version="1.6">
          <BatchSequence><Batch><Statements><StmtSimple><QueryPlan>
            <RelOp NodeId="0" PhysicalOp="Compute Scalar" LogicalOp="Compute Scalar">
              <OutputList />
              <ComputeScalar>
                <RelOp NodeId="1" PhysicalOp="Hash Match" LogicalOp="Aggregate">
                  <OutputList />
                  <Hash>
                    <RelOp NodeId="2" PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan">
                      <OutputList>
                        <ColumnReference Database="[db]" Schema="[reporting]" Table="[WorkOrderFacts]" Column="LineId" />
                        <ColumnReference Database="[db]" Schema="[reporting]" Table="[WorkOrderFacts]" Column="RepairMinutes" />
                      </OutputList>
                      <IndexScan Ordered="0">
                        <Object Database="[db]" Schema="[reporting]" Table="[WorkOrderFacts]" Index="[PK_WorkOrderFacts]" IndexKind="Clustered" />
                        <Predicate>
                          <ScalarOperator><Compare>
                            <ScalarOperator><Identifier><ColumnReference Database="[db]" Schema="[reporting]" Table="[WorkOrderFacts]" Column="CompletedAt" /></Identifier></ScalarOperator>
                            <ScalarOperator><Identifier><ColumnReference Column="@__from_0" /></Identifier></ScalarOperator>
                          </Compare></ScalarOperator>
                        </Predicate>
                      </IndexScan>
                    </RelOp>
                  </Hash>
                </RelOp>
              </ComputeScalar>
            </RelOp>
          </QueryPlan></StmtSimple></Statements></Batch></BatchSequence>
        </ShowPlanXML>
        """;

    [Fact]
    public void Operators_are_listed_with_the_index_for_data_access()
    {
        var plan = PlanXml.Parse(ScanPlan);

        Assert.Equal(
            ["Compute Scalar", "Hash Match", "Clustered Index Scan"],
            plan.Operators.Select(o => o.PhysicalOp));
        Assert.Equal("PK_WorkOrderFacts", plan.Operators[2].Index);
        Assert.Equal("Clustered Index Scan [PK_WorkOrderFacts] > Hash Match > Compute Scalar", plan.Describe());
    }

    [Fact]
    public void Needed_columns_come_from_the_output_list_and_the_predicate_of_the_table_only()
    {
        var plan = PlanXml.Parse(ScanPlan);

        Assert.Equal(["CompletedAt", "LineId", "RepairMinutes"], plan.NeededColumns("WorkOrderFacts"));
        Assert.Empty(plan.NeededColumns("SomethingElse"));
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        Assert.ThrowsAny<Exception>(() => PlanXml.Parse("not xml"));
        Assert.Throws<ArgumentException>(() => PlanXml.Parse(" "));
    }
}

public class StatsTests
{
    [Theory]
    [InlineData(new double[] { 3, 1, 2 }, 2)]
    [InlineData(new double[] { 4, 1, 3, 2 }, 2.5)]
    [InlineData(new double[] { 7 }, 7)]
    public void Median_is_the_middle_value(double[] values, double expected)
    {
        Assert.Equal(expected, Stats.Median(values));
    }

    [Fact]
    public void Median_works_for_integers_and_rejects_empty_input()
    {
        Assert.Equal(20, Stats.Median(new long[] { 10, 20, 900 }));
        Assert.Throws<InvalidOperationException>(() => Stats.Median(Array.Empty<int>()));
    }

    [Fact]
    public void Markdown_table_escapes_pipes_and_newlines()
    {
        var table = MarkdownTable.Render(["a", "b"], [["x | y", "line1\nline2"]]);

        Assert.Equal("| a | b |\n| --- | --- |\n| x \\| y | line1 line2 |\n".ReplaceLineEndings(), table.ReplaceLineEndings());
    }

    [Fact]
    public void Markdown_table_rejects_a_ragged_row()
    {
        Assert.Throws<ArgumentException>(() => MarkdownTable.Render(["a", "b"], [["only one"]]));
    }
}
