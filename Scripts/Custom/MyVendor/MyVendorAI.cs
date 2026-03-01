////////////////////////////////////////////
// Don't expect to discover a copyright   //
// for an illegal emulator use.           //
////////////////////////////////////////////
using System;
using System.Collections;
using Server.Targeting;
using Server.Network;

namespace Server.Mobiles
{
	public class MyVendorAI : VendorAI
	{
		//////////////////////
		// Turn it to false if you don't want special reactions...
		private static bool SpecilalSpeak=false;
		//////////////////////

		public MyVendorAI(BaseCreature m) : base (m)
		{
		}

		public override bool HandlesOnSpeech( Mobile from )
		{
			return ( from.Alive && from.InRange( m_Mobile.Location, 3 ) );
		}

		public override void OnSpeech( SpeechEventArgs e )
		{
			if (!SpecilalSpeak)
			{
				base.OnSpeech(e);
				return;
			}
			if ( !(m_Mobile is BaseVendor) )
			{
				return;
			}

			if ( !m_Mobile.InRange( e.Mobile, 1 ) )
			{
				return;
			}
			
			MyVendor vendor = (MyVendor)m_Mobile;
			Mobile   from   = e.Mobile;
              		string   speech = e.Speech.ToLower();

			// french grammar and vocabulary implementation

			bool _i        = (  (speech.IndexOf("je ")!=-1)
					 || (speech.IndexOf("j'")!=-1) );
			bool _you      = (  (speech.IndexOf("tu ")!=-1)
				 	 || (speech.IndexOf("vous ")!=-1));
			bool _sell     = (  (speech.IndexOf("vendez")!=-1)
					 || (speech.IndexOf("vendre")!=-1)
					 || (speech.IndexOf("vends")!=-1));
			bool _buy      =    (speech.IndexOf("achete")!=-1);
			bool _yes      =    (speech.IndexOf("oui")!=-1);
			bool _no       =    (speech.IndexOf("non")!=-1);
			bool _hello    = (  (speech.IndexOf("bonjour")!=-1)
					 || (speech.IndexOf("salut")!=-1));
			bool _bye      =    (speech.IndexOf("revoir")!=-1);
			bool _named    =    (speech.IndexOf(vendor.Name.ToLower())!=-1);
			bool _what     =    (speech.IndexOf("quel")!=-1);
			bool _how      =    (speech.IndexOf("comment")!=-1);
			bool _question =    (speech.IndexOf("?")!=-1);
			if (( _i && _buy ) || (_you && _sell)) {
				e.Handled = true;
				vendor.VendorBuy( from );
				vendor.FocusMob = from;
				switch ( Utility.Random( 5 ) )
				{
					default:
					case 0: vendor.Say("Voilà tout ce que je peux vous proposer."); break;
					case 1: vendor.Say("Regardez-moi tout ça! c'est pas beau ? hein ?"); break;
					case 2: vendor.Say("Aaaahh... Que de la qualité! Et pas cher! regardez!"); break;
					case 3: vendor.Say("Oui ? Que vous faut-il ?"); break;
					case 4: vendor.Say("Tout ce qui est ici est à vendre, regardez."); break;
				}
			} else if (( _i && _sell ) || (_you && _buy)) {
				e.Handled = true;
				vendor.VendorSell( from );
				vendor.FocusMob = from;
				switch ( Utility.Random( 5 ) )
				{
					default:
					case 0: vendor.Say("mmhhh... Voyons cela ?"); break;
					case 1: vendor.Say("Tout dépend de ce que vous me proposez comme marchandise."); break;
					case 2: vendor.Say("On reprend presque tout ici. Voyons ce que vous me proposez."); break;
					case 3: vendor.Say("Faites voir ce que vous voulez vendre ?"); break;
					case 4: vendor.Say("Alors, voyons cela."); break;
				}
			} else if ( _buy || _sell ) {
				vendor.FocusMob = from;
				vendor.Say("Des problèmes d'élocution ?");
			} else if (  (speech.IndexOf("vendor")!=-1)
				  || (speech.IndexOf("buy")!=-1)
				  || (speech.IndexOf("sell")!=-1)) {
				vendor.FocusMob = from;
				switch ( Utility.Random( 3 ) )
				{
					default:
					case 0: vendor.Say("Désolé, mais votre langue mais inconnu mon ptit..."); break;
					case 1: vendor.Emote("Parle fort");
						vendor.Say("Il y a un interprète par ici ?"); break;
					case 2: vendor.Say("Toi parler langue à moi ?"); break;
				}
			} else if ( _yes && _no) {
				vendor.FocusMob = from;
				vendor.Say("C'est plutôt flou...");
			} else if ( _yes ) {
				vendor.FocusMob = from;
				switch ( Utility.Random( 6 ) )
				{
					default:
					case 0: vendor.Say("Ah, très bien..."); break;
					case 1: vendor.Say("Heureux de l'entendre."); break;
					case 2: vendor.Say("Très bien."); break;
					case 3: vendor.Say("Vous êtes sûre ?"); break;
					case 4: vendor.Say("Ah, je le savais!"); break;
					case 5: vendor.Say("Si vous aviez dit non, cela m'aurait étonné."); break;
				}
			} else if ( _named && _hello ) {
				vendor.FocusMob = from;
				switch ( Utility.Random( 6 ) )
				{
					default:
					case 0: vendor.Say("Ah! bonjour "+from.Name+"! ça va ?"); break;
					case 1: vendor.Say("Alors, comment ça va aujourd'hui "+from.Name+"?"); break;
					case 2: vendor.Say("Tiens, vous revoilà. Bien le bonjour "+from.Name+"."); break;
					case 3: vendor.Say("Bonjour "+from.Name+"! Que puis-je pour vous ?"); break;
					case 4: vendor.Say("Ah, ça va aujourd'hui ?"); break;
					case 5: vendor.Say(from.Name+", quelle bonne surprise!"); break;
				}
			} else if ( _hello ) {
				vendor.FocusMob = from;
				switch ( Utility.Random( 6 ) )
				{
					default:
					case 0: vendor.Say("Bonjour à vous!"); break;
					case 1: vendor.Say("Bienvenue bienvenue!"); break;
					case 2: vendor.Say("Bonjour..."); break;
					case 3: vendor.Say("Entrez, entrez..."); break;
					case 4: vendor.Say("Bienvenue chez "+vendor.Name); break;
					case 5: vendor.Say("Bonjour, que puis-je pour vous ?"); break;
				}
			} else if ( (_what && (speech.IndexOf("nom")!=-1)) || ( _how && (speech.IndexOf("appel")!=-1)) ) {
				vendor.FocusMob = from;
				switch ( Utility.Random( 4 ) )
				{
					default:
					case 0: vendor.Say("Je m'appelle "+vendor.Name); break;
					case 1: vendor.Say("On m'appelle "+vendor.Name); break;
					case 2: vendor.Say("Je me nomme "+vendor.Name); break;
					case 3: vendor.Say("Mon nom est "+vendor.Name); break;
				}
			} else if ( (_i && (speech.IndexOf("appel")!=-1)) ||
				    (_i && (speech.IndexOf("nomme")!=-1)) ||
				   ((speech.IndexOf("mon")!=-1) && (speech.IndexOf("nom")!=-1)) ||
				   (_i && (speech.IndexOf("présente")!=-1)) ) {
				vendor.FocusMob = from;
				switch ( Utility.Random( 4 ) )
				{
					default:
					case 0: vendor.Say("Enchanté de vous connaitre."); break;
					case 1: vendor.Say("Content de faire votre connaissance."); break;
					case 2: vendor.Say("Et bien, enchanté, moi c'est "+vendor.Name+"."); break;
					case 3: vendor.Say("Enchanté! Je m'appelle "+vendor.Name+"!"); break;
				}
			} else if ( _question && 
				    ( (_you && (speech.IndexOf("bien")!=-1)) ||
				    (speech.IndexOf("ca va")!=-1) || (speech.IndexOf("ça va")!=-1) ) ) {
				vendor.FocusMob = from;
				switch ( Utility.Random( 4 ) )
				{
					default:
					case 0: vendor.Say("Je vais fort bien, et vous-même ?"); break;
					case 1: vendor.Say("Très bien, et vous ?"); break;
					case 2: vendor.Say("ça va, ça va..."); break;
					case 3: vendor.Say("Oh! très bien! Et vous-même ?"); break;
				}
			} else if ( _question ) {
				vendor.FocusMob = from;
				switch ( Utility.Random( 4 ) )
				{
					default:
					case 0: vendor.Say("Je ne sais pas."); break;
					case 1: vendor.Say("Mmmh... bonne question..."); break;
					case 2: vendor.Say("Et bien, aucune idée."); break;
					case 3: vendor.Say("Euh... je ne sais..."); break;
				}				
			} else if ( _bye ) {
				vendor.FocusMob = from;
				switch ( Utility.Random( 4 ) )
				{
					default:
					case 0: vendor.Say("Au revoir!"); break;
					case 1: vendor.Say("A bientôt j'espère!..."); break;
					case 2: vendor.Say("Au revoir à vous..."); break;
					case 3: vendor.Say("Au revoir, et merci!"); break;
				}				
			} else if ( _named ) {
				vendor.FocusMob = from;
				switch ( Utility.Random( 4 ) )
				{
					default:
					case 0: vendor.Say("Oui ? On m'appelle ?"); break;
					case 1: vendor.Say("Je suis là, oui ?"); break;
					case 2: vendor.Say("Vous désirez ?"); break;
					case 3: vendor.Say("On parle de moi ?"); break;
				}
			} else {
				vendor.FocusMob = from;
				switch ( Utility.Random( 10 ) )
				{
					default:
					case 0: vendor.Say("Ah..."); break;
					case 1: vendor.Say("Euh..."); break;
					case 2: vendor.Emote("se gratte la tête"); break;
					case 3: vendor.Emote("réfléchit"); break;
					case 4: vendor.Say("Si vous le dîtes..."); break;
					case 5: vendor.Say("Pardon ? Vous disiez ?"); break;
					case 6: vendor.Say("Effectivement..."); break;
					case 7: vendor.Say("Mmmhh... Et vous même ?"); break;
					case 8: vendor.Say("C'est à voir..."); break;
					case 9: vendor.Say("Peut-être..."); break;
				}				
			}
		}
	}
}