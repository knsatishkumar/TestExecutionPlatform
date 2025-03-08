using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestExecutionPlatform.Core.Models
{
    public class TestResult
    {
        public string Id { get; set; }
        public string JobId { get; set; }
        public string TestName { get; set; }
        public string Status { get; set; }
        public double Duration { get; set; }
        public string ErrorMessage { get; set; }
        public string StackTrace { get; set; }
    }
}
