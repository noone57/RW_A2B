﻿#region Usings

using System;
using System.Collections.Generic;
using A2B.Annotations;
using RimWorld;
using UnityEngine;
using Verse;

#endregion

namespace A2B
{

    [UsedImplicitly]
    public class BeltComponent : ThingComp
    {

        #region Fields/Properties
        //Changed from private to public for access from BeltItemContainer
        public BeltItemContainer ItemContainer;

        protected Phase _beltPhase;

        private IntVec3 _thingOrigin;

        protected Level _processLevel;
        protected Level _inputLevel;
        protected Level _outputLevel;

        public Phase BeltPhase
        {
            get { return _beltPhase; }
        }

		public Level ProcessLevel
		{
			get { return _processLevel; }
		}

        public Level InputLevel
        {
            get { return _inputLevel; }
        }

        public Level OutputLevel
        {
            get { return _outputLevel; }
        }

        public CompGlower GlowerComponent { get; set; }

		public CompPowerTrader PowerComponent { get; set; }

        public virtual int BeltSpeed {
            get { return A2BData.BeltSpeed.TicksToMove; }
        }

        public IntVec3 ThingOrigin
        {
            set { _thingOrigin = value; }
            get { return _thingOrigin; }
        }

        public bool Empty
        {
            get { return ItemContainer.Empty; }
        }

		#endregion

        public BeltComponent()
        {
            _processLevel = Level.Surface;
			_inputLevel = Level.Surface;
            _outputLevel = Level.Surface;
            _beltPhase = Phase.Offline;

            ItemContainer = new BeltItemContainer(this);
            ThingOrigin = IntVec3.Invalid;

			//BeltSpeed = Constants.DefaultBeltSpeed;
        }

        #region Temperature Stuff

        protected virtual void DoFreezeCheck()
        {
            float temp = GenTemperature.GetTemperatureForCell(parent.Position);

            if (BeltPhase == Phase.Frozen && temp > A2BData.Climatization.FreezeTemperature && Rand.Range(0.0f, 1.0f) < 0.50f)
                _beltPhase = Phase.Offline;

            if (BeltPhase != Phase.Frozen && Rand.Range(0.0f, 1.0f) < this.FreezeChance(temp))
                Freeze();

        }

        protected virtual void Freeze()
        {
            _beltPhase = Phase.Frozen;
            Messages.Message(Constants.TxtFrozenMsg.Translate(), MessageSound.Negative);

            MoteThrower.ThrowMicroSparks(Gen.TrueCenter(parent));
        }

        #endregion

        #region Routing Stuff

        /**
         *  Returns the location the thing should go. This is where the routing happens.
         **/
        public virtual IntVec3 GetDestinationForThing([NotNull] Thing thing)
        {
            return this.GetPositionFromRelativeRotation(Rot4.North);
        }

		// onlyCheckConnection - Skips all other checks except whether a physical link exists
		// between the two components.
		public virtual bool CanAcceptFrom( BeltComponent belt, bool onlyCheckConnection = false )
        {
            // If I can't accept from anyone, I certainly can't accept from you.
            /*
            if( !onlyCheckConnection )
                foreach( var thing in belt.ItemContainer.ThingsToMove )
                    if( !CanAcceptThing( thing ) )
                        return false;
            */

            // This belt isn't on the other belts output level
            if( belt.OutputLevel != this.InputLevel )
                return false;

            for (int i = 0; i < 4; ++i)
            {
                Rot4 dir = new Rot4(i);
                if (CanAcceptFrom(dir) && belt.parent.Position == this.GetPositionFromRelativeRotation(dir))
                    return true;
            }

            return false;
        }

        /**
         *  This method assumes that the component can accept in general - i.e. If it can accept at all, can
         *  it accept from the given direction? (If it accepts from the south, but it's currently clogged, this
         *  method still returns true)
         **/
        public virtual bool CanAcceptFrom(Rot4 direction)
        {
            return (direction == Rot4.South);
        }

        /**
         *  Returns whether the component can accept items at all. Useful for locking/disabling components without
         *  messing with directional routing code.
         **/
        public virtual bool CanAcceptThing( Thing thing )
        {
            if( BeltPhase != Phase.Active )
                return false;
            if( Empty )
                return true;
            foreach( var item in ItemContainer.ThingStatus )
            {
                if(
                    ( item.Status == MovementStatus.WaitClear )&&
                    ( item.Thing.def == thing.def )&&
                    ( item.Thing.stackCount < item.Thing.def.stackLimit )
                )
                {
                    item.Status = MovementStatus.WaitMerge;
                    return true;
                }
                if(
                    ( item.Status == MovementStatus.WaitMerge )&&
                    ( item.Merge == thing )
                )
                {
                    return true;
                }
            }
            return false;
        }

        /**
         *  Returns whether the component is allowed to output to anything other than a belt. Most can not - the unloader
         *  is an example of one that can, however.
         **/
        public virtual bool CanOutputToNonBelt( IntVec3 beltDest, Thing thing )
        {
            return false;
        }

        // This is a fast function for surface belts, underground belts must extend and override
        protected virtual void MoveThingTo([NotNull] Thing thing, IntVec3 beltDest)
        {
            OnBeginMove(thing, beltDest);

            if( CanOutputToNonBelt( beltDest, thing ) && beltDest.NoStorageBlockersIn( thing ) )
            {
                ItemContainer.DropItem(thing, beltDest);
            }
            else if ( OutputLevel == Level.Surface )
            {
				// Find a belt component at our output level
                var belt = beltDest.GetBeltSurfaceComponent();

                //  Check if there is a belt, if it can accept this thing
                if( belt == null || !belt.CanAcceptThing( thing ) )
                {
                    return;
                }

                ItemContainer.TransferItem(thing, belt.ItemContainer);

                // Need to check if it is a receiver or not ...
                belt.ThingOrigin = parent.Position;
            }
        }

        #endregion

        #region Drawing Stuff

        /*
        protected static void DrawGUIOverlay([NotNull] ThingStatus status, Vector3 drawPos )
        {
            if( Find.CameraMap.CurrentZoom != CameraZoomRange.Closest )
                return;

            var labelText = "";
            if( status.Thing.def.stackLimit > 1 )
                labelText = GenString.ToStringCached( status.Thing.stackCount );
            else
            {
                QualityCategory qc;
                if( !QualityUtility.TryGetQuality( status.Thing, out qc ) )
                    return;

                labelText = QualityUtility.GetLabelShort( qc );
            }

            drawPos.z += -0.4f;
            var screenPos = Find.CameraMap.camera.WorldToScreenPoint( drawPos );
            screenPos.y = Screen.height - screenPos.y;

            var labelPos = new Vector2( screenPos.x, screenPos.y );

            //Log.Message( "DrawGUIOverlay() :: " + status.Thing.ThingID + " - " + labelText + " @ " + drawPos.ToString() + " -> " + labelPos.ToString() );

            GenWorldUI.DrawThingLabel(
                labelPos,
                labelText,
                Color.white );

        }*/

        protected virtual Vector3 GetOffset([NotNull] ThingStatus status)
        {
            var destination = GetDestinationForThing(status.Thing);

            IntVec3 direction;
            if (ThingOrigin != IntVec3.Invalid)
            {
                direction = destination - ThingOrigin;
            }
            else
            {
                direction = parent.Rotation.FacingCell;
            }

            var progress = (float)status.Counter / BeltSpeed;

            if (Math.Abs(direction.x) == 1 && Math.Abs(direction.z) == 1 && ThingOrigin != IntVec3.Invalid)
            {
                // Diagonal movement
                var incoming = (parent.Position - ThingOrigin).ToVector3();
                var outgoing = (destination - parent.Position).ToVector3();

                // Now adjust the vectors.
                // Both need to be half the length so they only reach the edge of out square

                // The incoming vector also needs to be negated as it points in the wrong direction

                incoming = (-incoming) / 2;
                outgoing = outgoing / 2;

                var angle = progress * Mathf.PI / 2;

                var cos = Mathf.Cos(angle);
                var sin = Mathf.Sin(angle);

                return incoming * (1 - sin) + outgoing * (1 - cos);
            }

            var dir = direction.ToVector3();
            dir.Normalize();

            var scaleFactor = progress - .5f;

            return dir * scaleFactor;
        }

        #endregion

        #region Callbacks (Core)

        public override void PostDeSpawn ()
        {
            ItemContainer.Destroy();

            base.PostDeSpawn();
        }

        public override void PostSpawnSetup()
        {
            GlowerComponent = parent.GetComp<CompGlower>();
            PowerComponent = parent.GetComp<CompPowerTrader>();

            // init ice graphic
            Graphic g = BeltUtilities.IceGraphic;
            if (g == null)
                Log.ErrorOnce("IceGraphic was null!", 12);
        }

        public override void PostExposeData()
        {
			Scribe_Values.LookValue(ref _beltPhase, "phase");

            Scribe_Deep.LookDeep(ref ItemContainer, "container", this);

            Scribe_Values.LookValue(ref _thingOrigin, "thingOrigin", IntVec3.Invalid);
        }

        public override void PostDraw()
        {
            base.PostDraw();

            if (BeltPhase == Phase.Frozen)
            {
                this.DrawIceGraphic();
            }

            foreach (var status in ItemContainer.ThingStatus)
            {
                var drawPos = parent.DrawPos + GetOffset(status) + Altitudes.AltIncVect * Altitudes.AltitudeFor(AltitudeLayer.Item);

                status.Thing.DrawAt(drawPos);

                //DrawGUIOverlay(status, drawPos);
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if( parent.IsHashIntervalTick( 250 ) )
                ItemContainer.TickRare();

            if( parent.IsHashIntervalTick( A2BData.OccasionalTicks ) )
                OnOccasionalTick();

            if (BeltPhase == Phase.Frozen && Rand.Range(0.0f, 1.0f) < 0.05)
                MoteThrower.ThrowAirPuffUp(parent.DrawPos);

            if (BeltPhase == Phase.Jammed && Rand.Range(0.0f, 1.0f) < 0.05)
                MoteThrower.ThrowMicroSparks(parent.DrawPos);

            DoBeltTick();

            ItemContainer.Tick();
        }

        #endregion

        #region Callbacks (Custom)

        public virtual void OnOccasionalTick()
        {
            DoFreezeCheck();

            if (BeltPhase == Phase.Active || BeltPhase == Phase.Jammed)
                DoJamCheck();
        }

        /**
         *  Called after the ItemContainer's tick.
         **/
        protected virtual void PostItemContainerTick()
        {
            // stub
        }

        /**
         *  Called as soon as a destination is selected and the movement of that
         *  item begins.
         **/
        public virtual void OnBeginMove(Thing thing, IntVec3 dest) {
            // stub
        }

        /**
         *  Called immediately before transferring an item. Returning false here
         *  is your last chance to prevent the item from moving to the next belt.
         **/
        public virtual bool PreItemTransfer(Thing item, BeltComponent other) {
            return true;
        }

        /**
         * Called immediately after transferring an item. Useful for triggering
         * events every time items get transferred, like deterioration.
         **/
        public virtual void OnItemTransfer(Thing item, BeltComponent other)
        {
			if (Rand.Range(0.0f, 1.0f) < A2BData.Durability.DeteriorateChance)
                parent.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, Rand.RangeInclusive(0, 2), parent));
        }

        /**
         *  Called immediately after receiving an item.
         **/
        public virtual void OnItemReceived(Thing item, BeltComponent other) {
            // stub
        }


        #endregion

        private void DoBeltTick()
        {
			// Belts require power directly or infered through a physical link
            if( ( PowerComponent != null )&&
                ( PowerComponent.PowerOn ) )
            {
                // Power is on -> do work
                // ----------------------
                // phase == offline

                if (BeltPhase == Phase.Offline)
                {
                    // Turn on, incl. 'system online' glow
                    _beltPhase = Phase.Active;
					if( GlowerComponent != null )
                        GlowerComponent.UpdateLit();

					// If it's an underground belt, don't auto-pickup
					// as it has lost it's targeting vector
					if( ProcessLevel == Level.Underground )
						return;

					// Surface belts auto-pickup items on them.
					// Check if there is anything on the belt: yes? -> add it to our container
                    //foreach (var target in Find.Map.thingGrid.ThingsListAt(parent.Position))
                    foreach (var target in Find.Map.thingGrid.ThingsAt(parent.Position))
                    {
                        // Check and make sure this is not a Pawn, and not the belt itself !
                        if ((target.def.category == ThingCategory.Item) && (target != parent))
                        {
							ItemContainer.AddItem(target, BeltSpeed / 2);
                        }
                    }

                    //glowerComp.def.glowColor = new ColorInt(255,200,0,0); // Hum ... that changes ALL the belt ... not what I want ...
                    return;
                }

                // phase == active
                if (BeltPhase != Phase.Active)
                {
                    return;
                }

                // Active 'yellow' color
				if( GlowerComponent != null )
                    GlowerComponent.UpdateLit(); // in principle not required (should be already ON ...)

                ItemContainer.MoveTick();

                PostItemContainerTick();

                if (!ItemContainer.WorkToDo)
                {
                    return;
                }

                foreach (var thing in ItemContainer.ThingsToMove)
                {
                    // Alright, I have something to move. Where to ?
                    var beltDest = GetDestinationForThing(thing);
                    if (beltDest != IntVec3.Invalid)
                        MoveThingTo(thing, beltDest);
                }
            }
            else
            {
                // Power off -> reset everything
                // Let's be smart: check this only once, set the item to 'Unforbidden', and then, let the player choose what he wants to do
                // i.e. forbid or unforbid them ...

                // Already inactive and empty
				if( ( BeltPhase != Phase.Active )&&
                    ( ItemContainer.Empty == true ) )
                    return;

                // Turn glower off
				if( GlowerComponent != null )
                    GlowerComponent.UpdateLit();

                // Phase, inactive
                _beltPhase = Phase.Offline;

                // Only drop items for surface belts and underground belts without covers
                if( ( ProcessLevel == Level.Underground )&&
                    ( ( (BeltUndergroundComponent)this ).CoverIsOn == true ) )
                    return;

                // Drop items
                if( ItemContainer.Empty == false )
                    ItemContainer.DropAll(parent.Position, true);
            }
        }

        public virtual bool MovingThings()
        {
            return ItemContainer.MovingThings();
        }

        #region Power Stuff

        public virtual bool AllowLowPowerMode()
        {
            return true;
        }

        public virtual float GetBasePowerConsumption()
        {
            if( PowerComponent == null )
            {
                return 0f;
            }
            return PowerComponent.Props.basePowerConsumption;
        }

        #endregion

		#region Reliability Stuff

        public virtual void DoJamCheck()
        {
            if (BeltPhase == Phase.Jammed && parent.HitPoints == parent.MaxHitPoints)
            {
                _beltPhase = Phase.Offline;
                return;
            }

            if (BeltPhase == Phase.Active)
            {
                float healthPercent = (float)parent.HitPoints / (float)parent.MaxHitPoints;

                if (Rand.Range(0.0f, 1.0f) < this.JamChance(healthPercent))
                    Jam();
            }
        }

        public virtual void Jam()
        {
            int max = Rand.RangeInclusive(1, 3);
            for (int i = 0; i < max; ++i)
                MoteThrower.ThrowMicroSparks(parent.DrawPos);

            _beltPhase = Phase.Jammed;
            Messages.Message(Constants.TxtJammedMsg.Translate(), MessageSound.Negative);
        }

		#endregion

        [NotNull]
        public override string CompInspectStringExtra()
        {
			string statusText = Constants.TxtStatus.Translate() + " ";
            switch (BeltPhase)
            {
                case Phase.Offline:
                    statusText += Constants.TxtOffline.Translate();
                    break;
                case Phase.Active:
                    statusText += Constants.TxtActive.Translate();
                    break;
                case Phase.Frozen:
                    statusText += Constants.TxtFrozen.Translate();
                    break;
                case Phase.Jammed:
                    statusText += Constants.TxtJammed.Translate();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (ItemContainer.Empty)
            {
                return statusText;
            }

            return statusText
				+ "\n"
				+ Constants.TxtContents.Translate()
				+ " " + ItemContainer.ContentsString;
        }
    }
}
