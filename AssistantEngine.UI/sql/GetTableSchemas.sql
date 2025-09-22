

    -- Step 1: Collect metadata into a temporary table
    IF OBJECT_ID('tempdb..#TableSchema') IS NOT NULL
        DROP TABLE #TableSchema;

    CREATE TABLE #TableSchema (
        TableName NVARCHAR(128),
        ColumnName NVARCHAR(128),
        DataType NVARCHAR(128),
        ExampleValue NVARCHAR(255)
    );

    INSERT INTO #TableSchema (TableName, ColumnName, DataType)
    SELECT 
        t.name AS TableName,
        c.name AS ColumnName,
        tp.name AS DataType
    FROM 
        sys.tables AS t WITH (NOLOCK)
    INNER JOIN 
        sys.columns AS c WITH (NOLOCK) ON t.object_id = c.object_id
    INNER JOIN 
        sys.types AS tp WITH (NOLOCK) ON c.user_type_id = tp.user_type_id
    WHERE 
        t.name LIKE 'AIO_Exchange%' 

		   AND t.name NOT LIKE '%WalletBalances'
		   AND t.name NOT LIKE '%WalletTransactions%'
        AND t.name NOT LIKE '%deadlock%' 
        AND t.name NOT LIKE '%temp%'
        AND t.name NOT LIKE '%MERGE%' 
		   AND t.name NOT LIKE '%AUTO%' 
		   		   AND t.name NOT LIKE '%BACKUP%' 
        AND t.name NOT LIKE '%KEY%'
		    AND t.name NOT LIKE '%RPC%'
		 AND t.name NOT LIKE '%OLD%'
        AND tp.name != 'image';

    -- Step 2: Loop through the metadata to collect example values for string columns only
    DECLARE @TableName NVARCHAR(128),
            @ColumnName NVARCHAR(128),
            @DataType NVARCHAR(128),
            @SQL NVARCHAR(MAX);

    DECLARE cur CURSOR FOR
    SELECT TableName, ColumnName, DataType
    FROM #TableSchema;

    OPEN cur;
    FETCH NEXT FROM cur INTO @TableName, @ColumnName, @DataType;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @DataType IN ('char', 'varchar', 'nchar', 'nvarchar', 'text', 'ntext')
        BEGIN
            SET @SQL = 'UPDATE #TableSchema SET ExampleValue = (
                            SELECT TOP 1 CAST([' + @ColumnName + '] AS NVARCHAR(255))
                            FROM [' + @TableName + '] WITH (NOLOCK)
                            WHERE [' + @ColumnName + '] IS NOT NULL
                        )
                        WHERE TableName = ''' + @TableName + ''' AND ColumnName = ''' + @ColumnName + '''';
        
            EXEC sp_executesql @SQL;
        END

        FETCH NEXT FROM cur INTO @TableName, @ColumnName, @DataType;
    END;

    CLOSE cur;
    DEALLOCATE cur;

    -- Step 3: Select the results, including null ExampleValues for non-string columns
    SELECT * FROM #TableSchema 
    ORDER BY TableName, ColumnName;

    -- Clean up temporary table
    DROP TABLE #TableSchema;
