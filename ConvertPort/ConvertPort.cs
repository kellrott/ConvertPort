
using System;
using System.Collections;
using System.Collections.Generic;
using MindTouch.Deki.Script;
using MindTouch.Dream;
using MindTouch.Tasking;
using MindTouch.Deki;
using MindTouch.Xml;
using MindTouch.Deki.Script.Expr;
using System.Net;
using System.Web;
using System.Text;
using System.Xml;
using System.IO;
using System.Text.RegularExpressions;

using System.Reflection;
using System.CodeDom.Compiler;


namespace ConvertInterface
{
    public interface ConvertPlugin
    {
        XmlDocument ConvertAlias(string aliasStr);
    }
}



namespace MindTouch.Deki.Services.Extension {
	using Yield = IEnumerator<IYield>;
	
	[DreamService("Convert Port", "Copyright © 2010 University of California",
	    Info = "http://www.topsan.org/",
	    SID = new string[] { "sid://topsan.org/2010/07/extension/convert" }
	)]
	[DreamServiceConfig("apikey", "string", "Apikey to access deki")]
	[DreamServiceConfig("wikiid", "string?", "Wiki instance to query (default: default)")]
	[DreamServiceConfig("uri.deki", "uri?", "Uri of deki (default: http://localhost:8081/deki)")]
	//[DreamServiceConfig("semantic-config-uri", "uri", "URI of configuration file")]
	[DreamServiceBlueprint("deki/service-type", "extension")]
	[DekiExtLibrary(
	    Label = "ConvertPort",
	    Namespace = "convert",
	    Description = "Convert Port System"
	)]
	

	public class ConvertPort  : DekiExtService {		
		protected override Yield Start(XDoc config, Result result) {
			Result res;
			yield return res = Coroutine.Invoke(base.Start, config, new Result());
			res.Confirm();
						
	        result.Return();
		}			

		string [,]toHTML = { {"&amp;", "&amp;amp;" }, {"&gt;", "&amp;gt;" }, {"&lt;", "&amp;lt;" } };

		
		[DreamFeature("GET:convert", "Service to convert external data")]
		[DreamFeatureParam("method", "string?", "Method")]
		[DreamFeatureParam("id", "string?", "Page ID")]
		public Yield convert(DreamContext context, DreamMessage request, Result<DreamMessage> response) {	
			string methodStr = context.GetParam("method", string.Empty);
			string aliasStr = context.GetParam("id", string.Empty);

			XUri secPage = new XUri( context.ServerUri ).At( "deki", "pages", "=Convert:" + methodStr );
			Plug _dekiSec = Plug.New( secPage );
			XDoc secDoc = _dekiSec.Get().ToDocument();
			
			string userURL = secDoc["user.author/@href"].AsInnerText;
			Plug _dekiUser = Plug.New( userURL );
			XDoc userDoc = _dekiUser.Get().ToDocument();
			string perms = userDoc["permissions.user"]["operations"].AsInnerText;
			if ( !perms.Contains("UNSAFECONTENT") ) {
				response.Return( DreamMessage.Forbidden("UNSAFECONTENT not enabled") );
				yield break;
			}
			
			XUri codePage = new XUri( context.ServerUri ).At( "deki","pages", "=Convert:" + methodStr,"contents");
			Plug _deki = Plug.New( codePage, new TimeSpan(0,0,15) );
			string bodyText = _deki.With("format", "xhtml").Get().ToDocument()["body"]["pre"].Contents;
			
			for ( int i = 0; i < toHTML.GetLength(0); i++ ) {
				bodyText = Regex.Replace( bodyText, toHTML[i,0], toHTML[i,1] );
			}
			//string codeStr = HttpUtility.HtmlDecode( bodyText );	
			//XDoc doc = XDocFactory.From( codeStr, MimeType.ANY );
			//response.Return( DreamMessage.Ok( MimeType.TEXT, bodyText ) );
						
			Assembly scriptCode = CompileCode( bodyText, response );
			if ( scriptCode != null ) { 
				XmlDocument outDoc = runScript( scriptCode, aliasStr );
				response.Return( DreamMessage.Ok( MimeType.XML, new XDoc(outDoc) ) );
			}
			yield break;
			
		}
		
		
		private XmlDocument runScript( Assembly script, string alias ) {
			 // Now that we have a compiled script, lets run them
            foreach (Type type in script.GetExportedTypes()) {
                foreach (Type iface in type.GetInterfaces()) {
                    if (iface == typeof(ConvertInterface.ConvertPlugin)) {
                        // yay, we found a script interface, lets create it and run it!

                        // Get the constructor for the current type
                        // you can also specify what creation parameter types you want to pass to it,
                        // so you could possibly pass in data it might need, or a class that it can use to query the host application
                        ConstructorInfo constructor = type.GetConstructor(System.Type.EmptyTypes);
                        if (constructor != null && constructor.IsPublic) {
                            // lets be friendly and only do things legitimitely by only using valid constructors

                            // we specified that we wanted a constructor that doesn't take parameters, so don't pass parameters
                            ConvertInterface.ConvertPlugin scriptObject = constructor.Invoke(null) as ConvertInterface.ConvertPlugin;
                            if (scriptObject != null) {
                                //Lets run our script and display its results
                                //MessageBox.Show(scriptObject.RunScript(50));
								return scriptObject.ConvertAlias(alias);
                            } else {
                                // hmmm, for some reason it didn't create the object
                                // this shouldn't happen, as we have been doing checks all along, but we should
                                // inform the user something bad has happened, and possibly request them to send
                                // you the script so you can debug this problem
                            }
                        } else {
                            // and even more friendly and explain that there was no valid constructor
                            // found and thats why this script object wasn't run
                        }
                    }
				}
			}
			return null;
		}
			
		
		private Assembly CompileCode(string code, Result<DreamMessage> response)
        {
            // Create a code provider
            // This class implements the 'CodeDomProvider' class as its base. All of the current .Net languages (at least Microsoft ones)
            // come with thier own implemtation, thus you can allow the user to use the language of thier choice (though i recommend that
            // you don't allow the use of c++, which is too volatile for scripting use - memory leaks anyone?)
            Microsoft.CSharp.CSharpCodeProvider csProvider = new Microsoft.CSharp.CSharpCodeProvider();

            // Setup our options
            CompilerParameters options = new CompilerParameters();
            options.GenerateExecutable = false; // we want a Dll (or "Class Library" as its called in .Net)
            options.GenerateInMemory = true; // Saves us from deleting the Dll when we are done with it, though you could set this to false and save start-up time by next time by not having to re-compile
            // And set any others you want, there a quite a few, take some time to look through them all and decide which fit your application best!

            // Add any references you want the users to be able to access, be warned that giving them access to some classes can allow
            // harmful code to be written and executed. I recommend that you write your own Class library that is the only reference it allows
            // thus they can only do the things you want them to.
            // (though things like "System.Xml.dll" can be useful, just need to provide a way users can read a file to pass in to it)
            // Just to avoid bloatin this example to much, we will just add THIS program to its references, that way we don't need another
            // project to store the interfaces that both this class and the other uses. Just remember, this will expose ALL public classes to
            // the "script"
            options.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);
			//options.ReferencedAssemblies.Add( typeof(XDoc).Assembly.Location );
			//options.ReferencedAssemblies.Add( typeof(MindTouch.Dream).Assembly.Location );

			// Compile our code
            CompilerResults result;
            result = csProvider.CompileAssemblyFromSource(options, code);

            if (result.Errors.HasErrors)
            {
				string errorText = "";
				foreach (CompilerError error in result.Errors ) {
					errorText += error.ErrorText;
				}
				response.Return( DreamMessage.InternalError( errorText ) );
                return null;
            }

            if (result.Errors.HasWarnings)
            {
                // TODO: tell the user about the warnings, might want to prompt them if they want to continue
                // runnning the "script"
            }

            return result.CompiledAssembly;
        }
		
	}
	
}
