using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nest;
using NUnit.Framework;
using Nest.Tests.Integration.Yaml;


namespace Nest.Tests.Integration.Yaml.Delete
{
	public partial class DeleteTests
	{	


		public class InternalVersionTests : YamlTestsBase
		{
			[Test]
			public void InternalVersionTest()
			{	

				//do index 
				_body = new {
					foo= "bar"
				};
				this.Do(()=> this._client.IndexPost("test_1", "test", "1", _body));

				//match _response._version: 
				this.IsMatch(_response._version, 1);

				//do delete 
				this.Do(()=> this._client.Delete("test_1", "test", "1", nv=>nv
					.Add("version","2")
				));

				//do delete 
				this.Do(()=> this._client.Delete("test_1", "test", "1", nv=>nv
					.Add("version","1")
				));

				//match _response._version: 
				this.IsMatch(_response._version, 2);

			}
		}
	}
}

