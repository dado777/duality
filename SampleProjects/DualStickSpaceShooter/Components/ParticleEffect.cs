﻿using System;
using System.Collections.Generic;
using System.Linq;

using OpenTK;

using Duality;
using Duality.Editor;
using Duality.Resources;
using Duality.Components;
using Duality.Components.Renderers;
using Duality.Drawing;

namespace DualStickSpaceShooter
{
	[Serializable]
	[RequiredComponent(typeof(Transform))]
	public class ParticleEffect : Renderer, ICmpUpdatable
	{
		[Serializable]
		public struct EmissionData
		{
			public static readonly EmissionData Default = new EmissionData
			{
				Lifetime		= new Range(1000.0f, 3000.0f),
				BasePos			= Vector3.Zero,
				RandomPos		= 0.0f,
				RandomAngle		= new Range(0.0f, MathF.RadAngle360),
				BaseVel			= Vector3.Zero,
				RandomVel		= new Range(0.0f, 3.0f),
				RandomAngleVel	= new Range(-0.05f, 0.05f),
				MinColor		= ColorHsva.White,
				MaxColor		= ColorHsva.White
			};

			public Range		Lifetime;
			public Vector3		BasePos;
			public Range		RandomPos;
			public Range		RandomAngle; 
			public Vector3		BaseVel;
			public Range		RandomVel;
			public Range		RandomAngleVel;
			public ColorHsva	MinColor;
			public ColorHsva	MaxColor;
		}
		[Serializable]
		public struct EmissionPattern
		{
			public static readonly EmissionPattern Default = new EmissionPattern
			{
				Delay			= 100.0f,
				Count			= 1,
				MaxBurstCount	= -1
			};

			public Range	Delay;
			public Range	Count;
			public int		MaxBurstCount;
		}
		private struct Particle
		{
			public Vector3		Position;
			public Vector3		Velocity;
			public float		Angle;
			public float		AngleVelocity;
			public float		TimeToLive;
			public float		AgeFactor;
			public int			SpriteIndex;
			public ColorRgba	Color;
		}


		private ContentRef<Material>	material			= null;
		private Vector2					particleSize		= new Vector2(16, 16);
		private float					linearDrag			= 0.3f;
		private float					angularDrag			= 0.3f;
		private float					fadeOutAt			= 0.75f;
		private bool					disposeWhenEmpty	= true;
		private EmissionData			emitData			= EmissionData.Default;
		private EmissionPattern			emitPattern			= EmissionPattern.Default;

		private int						burstCount			= 0;
		private	float					burstTimer			= 0.0f;

		[NonSerialized]
		private float					boundRadius			= 0.0f;
		[NonSerialized]
		private RawList<Particle>		particles			= null;
		[NonSerialized]
		private RawList<VertexC1P3T2>	vertexBuffer		= null;

		
		public ContentRef<Material> ParticleMaterial
		{
			get { return this.material; }
			set { this.material = value; }
		}
		public Vector2 ParticleSize
		{
			get { return this.particleSize; }
			set { this.particleSize = value; }
		}
		[EditorHintRange(0.0f, 1.0f)]
		public float LinearDrag
		{
			get { return this.linearDrag; }
			set { this.linearDrag = value; }
		}
		[EditorHintRange(0.0f, 1.0f)]
		public float AngularDrag
		{
			get { return this.angularDrag; }
			set { this.angularDrag = value; }
		}
		[EditorHintRange(0.0f, 1.0f)]
		public float FadeOutAt
		{
			get { return this.fadeOutAt; }
			set { this.fadeOutAt = value; }
		}
		public bool DisposeWhenEmpty
		{
			get { return this.disposeWhenEmpty; }
			set { this.disposeWhenEmpty = true; }
		}
		public EmissionData EmitData
		{
			get { return this.emitData; }
			set { this.emitData = value; }
		}
		public EmissionPattern EmitPattern
		{
			get { return this.emitPattern; }
			set { this.emitPattern = value; }
		}
		public override float BoundRadius
		{
			get { return this.boundRadius * this.GameObj.Transform.Scale; }
		}


		public void AddParticles(int count)
		{
			Texture tex = this.RetrieveTexture();
			if (tex == null) return;
			Pixmap img = tex.BasePixmap.Res;
			if (img == null) return;

			int spriteFrameCount = img.AnimFrames;

			if (this.particles == null) this.particles = new RawList<Particle>(count);
			int oldCount = this.particles.Count;
			this.particles.Count = this.particles.Count + count;

			Particle[] particleData = this.particles.Data;
			for (int i = oldCount; i < this.particles.Count; i++)
			{
				this.InitParticle(ref particleData[i], spriteFrameCount);
			}
		}

		private void InitParticle(ref Particle particle, int frameCount)
		{
			Random random = MathF.Rnd;

			particle.AgeFactor		= 0.0f;
			particle.Position		= this.emitData.BasePos + random.NextVector3(this.emitData.RandomPos.MinValue, this.emitData.RandomPos.MaxValue);
			particle.Velocity		= this.emitData.BaseVel + random.NextVector3(this.emitData.RandomVel.MinValue, this.emitData.RandomVel.MaxValue);
			particle.Angle			= random.NextFloat(this.emitData.RandomAngle.MinValue, this.emitData.RandomAngle.MaxValue);
			particle.AngleVelocity	= random.NextFloat(this.emitData.RandomAngleVel.MinValue, this.emitData.RandomAngleVel.MaxValue);
			particle.TimeToLive		= random.NextFloat(this.emitData.Lifetime.MinValue, this.emitData.Lifetime.MaxValue);
			particle.SpriteIndex	= random.Next(frameCount);
			particle.Color			= random.NextColorHsva(this.emitData.MinColor, this.emitData.MaxColor).ToRgba();
		}
		private void RemoveParticle(int index)
		{
			this.particles.RemoveAt(index);
		}
		private Texture RetrieveTexture()
		{
			if (material.IsAvailable && material.Res.MainTexture.IsAvailable)
				return material.Res.MainTexture.Res;
			else
				return null;
		}

		public override void Draw(IDrawDevice device)
		{
			if (this.particles == null) return;
			
			Texture tex = this.RetrieveTexture();
			if (tex == null) return;

			float objAngle = this.GameObj.Transform.Angle;
			float objScale = this.GameObj.Transform.Scale;
			Vector3 objPos = this.GameObj.Transform.Pos;
			Vector2 particleHalfSize = this.particleSize * 0.5f;
			
			Vector2 objXDot, objYDot;
			MathF.GetTransformDotVec(objAngle, objScale, out objXDot, out objYDot);

			if (this.vertexBuffer == null) this.vertexBuffer = new RawList<VertexC1P3T2>(this.particles.Count * 4);
			this.vertexBuffer.Count = this.vertexBuffer.Count = this.particles.Count * 4;
			
			VertexC1P3T2[] vertexData = this.vertexBuffer.Data;
			Particle[] particleData = this.particles.Data;
			int particleCount = this.particles.Count;
			for (int i = 0; i < particleCount; i++)
			{
				ColorRgba color = particleData[i].Color;
				if (this.fadeOutAt < 1.0f)
				{
					float alpha = MathF.Clamp((1.0f - particleData[i].AgeFactor) / this.fadeOutAt, 0.0f, 1.0f);
					color.A = (byte)(alpha * 255.0f);
				}

				Rect uvRect;
				tex.LookupAtlas(particleData[i].SpriteIndex, out uvRect);

				Vector3 particlePos = particleData[i].Position;
				MathF.TransformDotVec(ref particlePos, ref objXDot, ref objYDot);
				particlePos += objPos;

				float particleAngle = objAngle + particleData[i].Angle;
				float particleScale = objScale;

				device.PreprocessCoords(ref particlePos, ref particleScale);

				Vector2 xDot, yDot;
				MathF.GetTransformDotVec(particleAngle, particleScale, out xDot, out yDot);

				Vector2 edgeTopLeft		= new Vector2(-particleHalfSize.X, -particleHalfSize.Y);
				Vector2 edgeBottomLeft	= new Vector2(-particleHalfSize.X, particleHalfSize.Y);
				Vector2 edgeBottomRight = new Vector2(particleHalfSize.X, particleHalfSize.Y);
				Vector2 edgeTopRight	= new Vector2(particleHalfSize.X, -particleHalfSize.Y);

				MathF.TransformDotVec(ref edgeTopLeft,		ref xDot, ref yDot);
				MathF.TransformDotVec(ref edgeBottomLeft,	ref xDot, ref yDot);
				MathF.TransformDotVec(ref edgeBottomRight,	ref xDot, ref yDot);
				MathF.TransformDotVec(ref edgeTopRight,		ref xDot, ref yDot);
				
				int vertexBaseIndex = i * 4;
				vertexData[vertexBaseIndex + 0].Pos.X = particlePos.X + edgeTopLeft.X;
				vertexData[vertexBaseIndex + 0].Pos.Y = particlePos.Y + edgeTopLeft.Y;
				vertexData[vertexBaseIndex + 0].Pos.Z = particlePos.Z;
				vertexData[vertexBaseIndex + 0].TexCoord.X = uvRect.X;
				vertexData[vertexBaseIndex + 0].TexCoord.Y = uvRect.Y;
				vertexData[vertexBaseIndex + 0].Color = color;

				vertexData[vertexBaseIndex + 1].Pos.X = particlePos.X + edgeBottomLeft.X;
				vertexData[vertexBaseIndex + 1].Pos.Y = particlePos.Y + edgeBottomLeft.Y;
				vertexData[vertexBaseIndex + 1].Pos.Z = particlePos.Z;
				vertexData[vertexBaseIndex + 1].TexCoord.X = uvRect.X;
				vertexData[vertexBaseIndex + 1].TexCoord.Y = uvRect.MaximumY;
				vertexData[vertexBaseIndex + 1].Color = color;

				vertexData[vertexBaseIndex + 2].Pos.X = particlePos.X + edgeBottomRight.X;
				vertexData[vertexBaseIndex + 2].Pos.Y = particlePos.Y + edgeBottomRight.Y;
				vertexData[vertexBaseIndex + 2].Pos.Z = particlePos.Z;
				vertexData[vertexBaseIndex + 2].TexCoord.X = uvRect.MaximumX;
				vertexData[vertexBaseIndex + 2].TexCoord.Y = uvRect.MaximumY;
				vertexData[vertexBaseIndex + 2].Color = color;
				
				vertexData[vertexBaseIndex + 3].Pos.X = particlePos.X + edgeTopRight.X;
				vertexData[vertexBaseIndex + 3].Pos.Y = particlePos.Y + edgeTopRight.Y;
				vertexData[vertexBaseIndex + 3].Pos.Z = particlePos.Z;
				vertexData[vertexBaseIndex + 3].TexCoord.X = uvRect.MaximumX;
				vertexData[vertexBaseIndex + 3].TexCoord.Y = uvRect.Y;
				vertexData[vertexBaseIndex + 3].Color = color;
			}

			device.AddVertices(this.material, VertexMode.Quads, vertexData, this.vertexBuffer.Count);
		}
		void ICmpUpdatable.OnUpdate()
		{
			// Update all existing particles
			Vector3 boundMax = Vector3.Zero;
			if (this.particles != null)
			{
				float timeMult = Time.TimeMult;
				float timePassed = Time.MsPFMult * timeMult;

				Particle[] particleData = this.particles.Data;
				int particleCount = this.particles.Count;
				for (int i = particleCount - 1; i >= 0; i--)
				{
					particleData[i].Position		+= particleData[i].Velocity * timeMult;
					particleData[i].Angle			+= particleData[i].AngleVelocity * timeMult;
					particleData[i].Velocity		-= particleData[i].Velocity * this.linearDrag * 0.1f * timeMult;
					particleData[i].AngleVelocity	-= particleData[i].AngleVelocity * this.angularDrag * 0.1f * timeMult;
					particleData[i].AgeFactor		+= timePassed / particleData[i].TimeToLive;
					if (particleData[i].AgeFactor > 1.0f)
						this.RemoveParticle(i);

					boundMax.X = MathF.Max(boundMax.X, MathF.Abs(particleData[i].Position.X));
					boundMax.Y = MathF.Max(boundMax.Y, MathF.Abs(particleData[i].Position.Y));
					boundMax.Z = MathF.Max(boundMax.Z, MathF.Abs(particleData[i].Position.Z));
				}
			}
			this.boundRadius = boundMax.Length + this.particleSize.Length;

			// Update particle emission
			if (this.burstCount < this.emitPattern.MaxBurstCount || this.emitPattern.MaxBurstCount < 0)
			{
				this.burstTimer -= Time.MsPFMult * Time.TimeMult;
				if (this.burstTimer <= 0.0f)
				{
					this.burstTimer += MathF.Rnd.NextFloat(this.emitPattern.Delay.MinValue, this.emitPattern.Delay.MaxValue);
					this.burstCount++;

					int count = MathF.Rnd.Next((int)this.emitPattern.Count.MinValue, (int)this.emitPattern.Count.MaxValue);
					this.AddParticles(count);
				}
			}

			// Dispose when empty
			if (this.disposeWhenEmpty && (this.particles == null || this.particles.Count == 0))
			{
				this.GameObj.DisposeLater();
			}
		}
	}
}