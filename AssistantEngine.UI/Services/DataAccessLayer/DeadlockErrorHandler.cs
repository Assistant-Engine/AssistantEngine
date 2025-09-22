using System;
using System.Data.SqlClient;

namespace AssistantEngine.DataAccessLayer
{
    public class DeadlockErrorHandler
    {
        private static int DEADLOCK_DELAY = 100;
        public static void ExecuteWithRetryAndHandle(Action action, int maxRetries = 3)
        {
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    action();
                    return; // Success, exit the loop and method
                }
                catch (SqlException ex) when (ex.Number == 1205)
                {
                    if (ex.Number == 1205)
                    {
                        System.Threading.Thread.Sleep(DEADLOCK_DELAY);
                        // Log retry attempt here
                        if (retry == maxRetries - 1)
                        {
                            Console.WriteLine("unable to handle deadlock exception");
                        }
                        //throw new DatabaseDeadlockException("Maximum retry limit reached after deadlock.", ex);
                    }
                    else
                    {
                        Console.WriteLine($"Unhandled Exception {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                    }
                }
            }

        }
    }
}

